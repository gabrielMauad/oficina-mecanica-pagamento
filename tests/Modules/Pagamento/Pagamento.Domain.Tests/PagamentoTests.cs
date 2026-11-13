using Pagamento.Domain;
using Pagamento.Domain.Events;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Domain.Tests;

public class PagamentoTests
{
    private static readonly Guid OrdemServicoId = Guid.NewGuid();
    private static readonly Guid OrcamentoId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    private static PagamentoEntity NovoPendente(decimal valor = 350.75m) =>
        PagamentoEntity.Criar(OrdemServicoId, OrcamentoId, ClienteId, valor).Value;

    [Fact(DisplayName = "Criar — nasce Pendente com os dados informados e sem eventos")]
    public void Criar_DeveNascerPendente()
    {
        // Act
        var result = PagamentoEntity.Criar(OrdemServicoId, OrcamentoId, ClienteId, 350.75m);

        // Assert
        Assert.True(result.IsSuccess);
        var pagamento = result.Value;
        Assert.NotEqual(Guid.Empty, pagamento.Id.Value);
        Assert.Equal(OrdemServicoId, pagamento.OrdemServicoId);
        Assert.Equal(OrcamentoId, pagamento.OrcamentoId);
        Assert.Equal(ClienteId, pagamento.ClienteId);
        Assert.Equal(350.75m, pagamento.Valor);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        Assert.Null(pagamento.MotivoRecusa);
        Assert.Null(pagamento.MercadoPagoPaymentId);
        Assert.Equal(pagamento.CriadoEm, pagamento.AtualizadoEm);
        Assert.InRange(pagamento.CriadoEm, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.Empty(pagamento.DomainEvents);
    }

    [Fact(DisplayName = "Criar — ordem de serviço vazia retorna erro de validação")]
    public void Criar_OrdemServicoVazia_DeveFalhar()
    {
        var result = PagamentoEntity.Criar(Guid.Empty, OrcamentoId, ClienteId, 10m);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.OrdemServicoInvalida", result.Error.Code);
    }

    [Fact(DisplayName = "Criar — orçamento vazio retorna erro de validação")]
    public void Criar_OrcamentoVazio_DeveFalhar()
    {
        var result = PagamentoEntity.Criar(OrdemServicoId, Guid.Empty, ClienteId, 10m);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.OrcamentoInvalido", result.Error.Code);
    }

    [Fact(DisplayName = "Criar — cliente vazio retorna erro de validação")]
    public void Criar_ClienteVazio_DeveFalhar()
    {
        var result = PagamentoEntity.Criar(OrdemServicoId, OrcamentoId, Guid.Empty, 10m);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.ClienteInvalido", result.Error.Code);
    }

    [Theory(DisplayName = "Criar — valor zero ou negativo retorna erro de validação")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Criar_ValorNaoPositivo_DeveFalhar(double valor)
    {
        var result = PagamentoEntity.Criar(OrdemServicoId, OrcamentoId, ClienteId, (decimal)valor);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.ValorInvalido", result.Error.Code);
    }

    [Fact(DisplayName = "Aprovar — de Pendente vai a Aprovado, guarda o id do Mercado Pago e levanta PagamentoAprovado")]
    public void Aprovar_DePendente_DeveAprovar()
    {
        // Arrange
        var pagamento = NovoPendente();

        // Act
        var result = pagamento.Aprovar(98765432101);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Equal(98765432101, pagamento.MercadoPagoPaymentId);
        Assert.Null(pagamento.MotivoRecusa);
        Assert.InRange(pagamento.AtualizadoEm, pagamento.CriadoEm, DateTime.UtcNow);

        var evento = Assert.IsType<PagamentoAprovado>(Assert.Single(pagamento.DomainEvents));
        Assert.Equal(pagamento.Id, evento.PagamentoId);
        Assert.Equal(OrdemServicoId, evento.OrdemServicoId);
        Assert.Equal(OrcamentoId, evento.OrcamentoId);
    }

    [Fact(DisplayName = "Recusar — de Pendente vai a Recusado, guarda o motivo e levanta PagamentoRecusado")]
    public void Recusar_DePendente_DeveRecusar()
    {
        // Arrange
        var pagamento = NovoPendente();

        // Act
        var result = pagamento.Recusar("cc_rejected_other_reason", 12345678901);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
        Assert.Equal("cc_rejected_other_reason", pagamento.MotivoRecusa);
        Assert.Equal(12345678901, pagamento.MercadoPagoPaymentId);

        var evento = Assert.IsType<PagamentoRecusado>(Assert.Single(pagamento.DomainEvents));
        Assert.Equal(pagamento.Id, evento.PagamentoId);
        Assert.Equal(OrdemServicoId, evento.OrdemServicoId);
        Assert.Equal(OrcamentoId, evento.OrcamentoId);
        Assert.Equal("cc_rejected_other_reason", evento.Motivo);
    }

    [Fact(DisplayName = "Recusar — aceita recusa sem id do Mercado Pago")]
    public void Recusar_SemIdDoMercadoPago_DeveRecusar()
    {
        var pagamento = NovoPendente();

        var result = pagamento.Recusar("motivo", mercadoPagoPaymentId: null);

        Assert.True(result.IsSuccess);
        Assert.Null(pagamento.MercadoPagoPaymentId);
    }

    [Theory(DisplayName = "Recusar — motivo vazio retorna erro e mantém Pendente")]
    [InlineData("")]
    [InlineData("   ")]
    public void Recusar_MotivoVazio_DeveFalhar(string motivo)
    {
        var pagamento = NovoPendente();

        var result = pagamento.Recusar(motivo, 1);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.MotivoVazio", result.Error.Code);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        Assert.Empty(pagamento.DomainEvents);
    }

    [Fact(DisplayName = "Aprovar — de Aprovado retorna conflito e não levanta novo evento")]
    public void Aprovar_DeAprovado_DeveFalhar()
    {
        var pagamento = NovoPendente();
        pagamento.Aprovar(1);
        pagamento.ClearDomainEvents();

        var result = pagamento.Aprovar(2);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.StatusInvalido", result.Error.Code);
        Assert.Equal(1, pagamento.MercadoPagoPaymentId);
        Assert.Empty(pagamento.DomainEvents);
    }

    [Fact(DisplayName = "Recusar — de Aprovado retorna conflito")]
    public void Recusar_DeAprovado_DeveFalhar()
    {
        var pagamento = NovoPendente();
        pagamento.Aprovar(1);

        var result = pagamento.Recusar("motivo", 2);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.StatusInvalido", result.Error.Code);
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Null(pagamento.MotivoRecusa);
    }

    [Fact(DisplayName = "Aprovar — de Recusado retorna conflito")]
    public void Aprovar_DeRecusado_DeveFalhar()
    {
        var pagamento = NovoPendente();
        pagamento.Recusar("motivo", 1);

        var result = pagamento.Aprovar(2);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.StatusInvalido", result.Error.Code);
        Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
    }

    [Fact(DisplayName = "Recusar — de Recusado retorna conflito")]
    public void Recusar_DeRecusado_DeveFalhar()
    {
        var pagamento = NovoPendente();
        pagamento.Recusar("motivo", 1);

        var result = pagamento.Recusar("outro motivo", 2);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.StatusInvalido", result.Error.Code);
        Assert.Equal("motivo", pagamento.MotivoRecusa);
    }

    [Fact(DisplayName = "Cancelar — de Pendente vai a Cancelado, guarda o motivo e não levanta evento")]
    public void Cancelar_DePendente_DeveCancelar()
    {
        // Arrange
        var pagamento = NovoPendente();

        // Act
        var result = pagamento.Cancelar("Estoque insuficiente");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(StatusPagamento.Cancelado, pagamento.Status);
        Assert.Equal("Estoque insuficiente", pagamento.MotivoRecusa);
        Assert.Null(pagamento.MercadoPagoPaymentId);
        Assert.InRange(pagamento.AtualizadoEm, pagamento.CriadoEm, DateTime.UtcNow);
        Assert.Empty(pagamento.DomainEvents);
    }

    [Theory(DisplayName = "Cancelar — motivo vazio retorna erro e mantém Pendente")]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancelar_MotivoVazio_DeveFalhar(string motivo)
    {
        var pagamento = NovoPendente();

        var result = pagamento.Cancelar(motivo);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.MotivoVazio", result.Error.Code);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
    }

    [Fact(DisplayName = "Cancelar — de Aprovado retorna conflito")]
    public void Cancelar_DeAprovado_DeveFalhar()
    {
        var pagamento = NovoPendente();
        pagamento.Aprovar(1);

        var result = pagamento.Cancelar("motivo");

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.StatusInvalido", result.Error.Code);
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
    }

    [Fact(DisplayName = "Cancelar — de Recusado retorna conflito")]
    public void Cancelar_DeRecusado_DeveFalhar()
    {
        var pagamento = NovoPendente();
        pagamento.Recusar("recusa", 1);

        var result = pagamento.Cancelar("motivo");

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.StatusInvalido", result.Error.Code);
        Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
        Assert.Equal("recusa", pagamento.MotivoRecusa);
    }

    [Fact(DisplayName = "Cancelar — de Cancelado retorna conflito")]
    public void Cancelar_DeCancelado_DeveFalhar()
    {
        var pagamento = NovoPendente();
        pagamento.Cancelar("primeiro");

        var result = pagamento.Cancelar("segundo");

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.StatusInvalido", result.Error.Code);
        Assert.Equal("primeiro", pagamento.MotivoRecusa);
    }

    [Fact(DisplayName = "Aprovar e Recusar — de Cancelado retornam conflito")]
    public void AprovarERecusar_DeCancelado_DevemFalhar()
    {
        var pagamento = NovoPendente();
        pagamento.Cancelar("motivo");

        var aprovar = pagamento.Aprovar(1);
        var recusar = pagamento.Recusar("outro", 2);

        Assert.True(aprovar.IsFailure);
        Assert.True(recusar.IsFailure);
        Assert.Equal(StatusPagamento.Cancelado, pagamento.Status);
        Assert.Empty(pagamento.DomainEvents);
    }

    [Fact(DisplayName = "Reconstituir — restaura o estado persistido sem levantar eventos")]
    public void Reconstituir_DeveRestaurarEstado()
    {
        // Arrange
        var id = PagamentoId.Novo();
        var criadoEm = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var atualizadoEm = criadoEm.AddMinutes(5);

        // Act
        var pagamento = PagamentoEntity.Reconstituir(
            id, OrdemServicoId, OrcamentoId, ClienteId, 99.9m,
            StatusPagamento.Recusado, "motivo", 42, criadoEm, atualizadoEm);

        // Assert
        Assert.Equal(id, pagamento.Id);
        Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
        Assert.Equal("motivo", pagamento.MotivoRecusa);
        Assert.Equal(42, pagamento.MercadoPagoPaymentId);
        Assert.Equal(99.9m, pagamento.Valor);
        Assert.Equal(criadoEm, pagamento.CriadoEm);
        Assert.Equal(atualizadoEm, pagamento.AtualizadoEm);
        Assert.Empty(pagamento.DomainEvents);
    }

    [Fact(DisplayName = "PagamentoId.Novo — gera ids distintos")]
    public void PagamentoId_Novo_DeveGerarIdsDistintos()
    {
        Assert.NotEqual(PagamentoId.Novo(), PagamentoId.Novo());
    }
}
