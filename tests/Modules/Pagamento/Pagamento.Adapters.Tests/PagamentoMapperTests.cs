using Pagamento.Adapters.DataSources.Mappers;
using Pagamento.Adapters.DataSources.Records;
using Pagamento.Adapters.Models.Requests;
using Pagamento.Adapters.Presenters;
using Pagamento.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Adapters.Tests;

public class PagamentoMapperTests
{
    [Theory(DisplayName = "Mapper — ida e volta preserva todos os campos em qualquer status")]
    [InlineData(StatusPagamento.Pendente, null, null)]
    [InlineData(StatusPagamento.Aprovado, null, 4242L)]
    [InlineData(StatusPagamento.Recusado, "cc_rejected_other_reason", 7L)]
    public void Mapper_IdaEVolta_DevePreservarCampos(StatusPagamento status, string? motivo, long? paymentId)
    {
        // Arrange
        var criadoEm = new DateTime(2026, 11, 10, 8, 30, 0, DateTimeKind.Utc);
        var original = PagamentoEntity.Reconstituir(
            PagamentoId.Novo(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1234.56m,
            status, motivo, paymentId, criadoEm, criadoEm.AddMinutes(1));

        // Act
        var record = PagamentoMapper.ToRecord(original);
        var restaurado = PagamentoMapper.ToDomain(record);

        // Assert
        Assert.Equal(status.ToString(), record.Status);
        Assert.Equal(original.Id, restaurado.Id);
        Assert.Equal(original.OrdemServicoId, restaurado.OrdemServicoId);
        Assert.Equal(original.OrcamentoId, restaurado.OrcamentoId);
        Assert.Equal(original.ClienteId, restaurado.ClienteId);
        Assert.Equal(1234.56m, restaurado.Valor);
        Assert.Equal(status, restaurado.Status);
        Assert.Equal(motivo, restaurado.MotivoRecusa);
        Assert.Equal(paymentId, restaurado.MercadoPagoPaymentId);
        Assert.Equal(original.CriadoEm, restaurado.CriadoEm);
        Assert.Equal(original.AtualizadoEm, restaurado.AtualizadoEm);
        Assert.Empty(restaurado.DomainEvents);
    }

    [Fact(DisplayName = "Mapper — status desconhecido no record lança ArgumentException")]
    public void ToDomain_StatusDesconhecido_DeveLancar()
    {
        var record = new PagamentoRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m,
            "Inexistente", null, null, DateTime.UtcNow, DateTime.UtcNow);

        Assert.Throws<ArgumentException>(() => PagamentoMapper.ToDomain(record));
    }

    [Fact(DisplayName = "Presenter — expõe os campos do contrato com o status em texto")]
    public void Presenter_DeveExporCamposDoContrato()
    {
        var pagamento = PagamentoEntity.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 80m).Value;
        pagamento.Recusar("cc_rejected_other_reason", 9);

        var viewModel = PagamentoPresenter.Present(pagamento);

        Assert.Equal(pagamento.Id.Value, viewModel.PagamentoId);
        Assert.Equal(pagamento.OrdemServicoId, viewModel.OrdemServicoId);
        Assert.Equal(pagamento.OrcamentoId, viewModel.OrcamentoId);
        Assert.Equal(80m, viewModel.Valor);
        Assert.Equal("Recusado", viewModel.Status);
        Assert.Equal("cc_rejected_other_reason", viewModel.MotivoRecusa);
        Assert.Equal(pagamento.CriadoEm, viewModel.CriadoEm);
        Assert.Equal(pagamento.AtualizadoEm, viewModel.AtualizadoEm);
    }

    [Fact(DisplayName = "Presenter — lista apresenta cada pagamento")]
    public void Presenter_Lista_DeveApresentarTodos()
    {
        IReadOnlyList<PagamentoEntity> pagamentos =
        [
            PagamentoEntity.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m).Value,
            PagamentoEntity.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2m).Value
        ];

        var lista = PagamentoPresenter.PresentListar(pagamentos);

        Assert.Equal(2, lista.Count);
        Assert.All(lista, vm => Assert.Equal("Pendente", vm.Status));
    }

    [Fact(DisplayName = "PagarRequest — carrega o titular do cartão")]
    public void PagarRequest_DeveCarregarTitular()
    {
        Assert.Equal("APRO", new PagarRequest("APRO").TitularCartao);
    }
}
