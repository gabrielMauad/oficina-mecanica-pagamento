using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pagamento.Application.Commands.CancelarPagamento;
using Pagamento.Application.Gateways;
using Pagamento.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Tests.Commands;

public class CancelarPagamentoHandlerTests
{
    private readonly Mock<IPagamentoGateway> _gatewayMock = new();
    private readonly CancelarPagamentoHandler _handler;

    private readonly Guid _ordemServicoId = Guid.NewGuid();
    private readonly Guid _orcamentoId = Guid.NewGuid();

    public CancelarPagamentoHandlerTests()
    {
        _handler = new CancelarPagamentoHandler(_gatewayMock.Object, NullLogger<CancelarPagamentoHandler>.Instance);
        _gatewayMock
            .Setup(g => g.AtualizarSePendente(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private PagamentoEntity ExistenteNoStatus(StatusPagamento status)
    {
        var pagamento = PagamentoEntity.Reconstituir(
            PagamentoId.Novo(), _ordemServicoId, _orcamentoId, Guid.NewGuid(), 250m, status,
            status == StatusPagamento.Recusado ? "motivo" : null, null, DateTime.UtcNow, DateTime.UtcNow);
        _gatewayMock
            .Setup(g => g.ObterPorOrdemServicoEOrcamento(_ordemServicoId, _orcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);
        return pagamento;
    }

    private Task<SharedKernel.Domain.Result<bool>> Cancelar(string motivo = "Estoque insuficiente") =>
        _handler.Handle(new CancelarPagamentoCommand(_ordemServicoId, _orcamentoId, motivo), CancellationToken.None);

    [Fact(DisplayName = "Pagamento Pendente — vai a Cancelado com o motivo, persiste e não levanta evento")]
    public async Task Handle_Pendente_DeveCancelar()
    {
        // Arrange
        var pagamento = ExistenteNoStatus(StatusPagamento.Pendente);

        // Act
        var result = await Cancelar("Estoque insuficiente");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(StatusPagamento.Cancelado, pagamento.Status);
        Assert.Equal("Estoque insuficiente", pagamento.MotivoRecusa);
        Assert.Empty(pagamento.DomainEvents);
        _gatewayMock.Verify(g => g.AtualizarSePendente(pagamento, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Sem pagamento do orçamento — sucesso sem efeito, nada é gravado")]
    public async Task Handle_SemPagamento_NaoDeveFazerNada()
    {
        // Arrange
        _gatewayMock
            .Setup(g => g.ObterPorOrdemServicoEOrcamento(_ordemServicoId, _orcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoEntity?)null);

        // Act
        var result = await Cancelar();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        _gatewayMock.Verify(
            g => g.AtualizarSePendente(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory(DisplayName = "Pagamento já resolvido ou cancelado (reentrega) — sucesso sem efeito, nada é gravado")]
    [InlineData(StatusPagamento.Aprovado)]
    [InlineData(StatusPagamento.Recusado)]
    [InlineData(StatusPagamento.Cancelado)]
    public async Task Handle_NaoPendente_NaoDeveFazerNada(StatusPagamento status)
    {
        // Arrange
        var pagamento = ExistenteNoStatus(status);

        // Act
        var result = await Cancelar();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(status, pagamento.Status);
        _gatewayMock.Verify(
            g => g.AtualizarSePendente(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Gravação condicional perdida (pagamento resolvido em paralelo) — sucesso sem efeito")]
    public async Task Handle_GravacaoCondicionalFalha_NaoDeveLancar()
    {
        // Arrange
        var pagamento = ExistenteNoStatus(StatusPagamento.Pendente);
        _gatewayMock
            .Setup(g => g.AtualizarSePendente(pagamento, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await Cancelar();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact(DisplayName = "Validator — ids e motivo vazios são rejeitados; comando válido é aceito")]
    public void Validator_DeveValidarComando()
    {
        var validator = new CancelarPagamentoValidator();

        Assert.False(validator.Validate(new CancelarPagamentoCommand(Guid.Empty, Guid.NewGuid(), "m")).IsValid);
        Assert.False(validator.Validate(new CancelarPagamentoCommand(Guid.NewGuid(), Guid.Empty, "m")).IsValid);
        Assert.False(validator.Validate(new CancelarPagamentoCommand(Guid.NewGuid(), Guid.NewGuid(), "")).IsValid);
        Assert.True(validator.Validate(new CancelarPagamentoCommand(Guid.NewGuid(), Guid.NewGuid(), "m")).IsValid);
    }
}
