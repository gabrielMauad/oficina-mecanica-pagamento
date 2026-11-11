using Pagamento.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Gateways;

/// <summary>
/// Port de persistência dos pagamentos. A Application não conhece o banco que os guarda.
/// </summary>
public interface IPagamentoGateway
{
    Task Adicionar(PagamentoEntity pagamento, CancellationToken ct = default);

    Task Atualizar(PagamentoEntity pagamento, CancellationToken ct = default);

    Task<PagamentoEntity?> ObterPorOrdemServicoEOrcamento(Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default);

    /// <summary>Pagamento ainda <see cref="StatusPagamento.Pendente"/> da ordem de serviço, ou <c>null</c>.</summary>
    Task<PagamentoEntity?> ObterPendentePorOrdemServico(Guid ordemServicoId, CancellationToken ct = default);

    Task<IReadOnlyList<PagamentoEntity>> ListarPorOrdemServico(Guid ordemServicoId, CancellationToken ct = default);
}
