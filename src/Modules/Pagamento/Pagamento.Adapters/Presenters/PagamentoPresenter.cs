using Pagamento.Adapters.Models.ViewModels;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Adapters.Presenters;

public static class PagamentoPresenter
{
    public static PagamentoViewModel Present(PagamentoEntity pagamento) =>
        new(
            pagamento.Id.Value,
            pagamento.OrdemServicoId,
            pagamento.OrcamentoId,
            pagamento.Valor,
            pagamento.Status.ToString(),
            pagamento.MotivoRecusa,
            pagamento.CriadoEm,
            pagamento.AtualizadoEm);

    public static List<PagamentoViewModel> PresentListar(IReadOnlyList<PagamentoEntity> pagamentos) =>
        [.. pagamentos.Select(Present)];
}
