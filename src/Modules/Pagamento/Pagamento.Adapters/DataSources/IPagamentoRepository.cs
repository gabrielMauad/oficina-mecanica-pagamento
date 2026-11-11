using Pagamento.Adapters.DataSources.Records;

namespace Pagamento.Adapters.DataSources;

/// <summary>
/// Fonte de dados dos pagamentos (implementada na camada de infraestrutura sobre o DynamoDB).
/// A chave de um pagamento é o par (ordem de serviço, orçamento).
/// </summary>
public interface IPagamentoRepository
{
    /// <summary>Grava o pagamento; falha se já existir um para o mesmo par (ordem de serviço, orçamento).</summary>
    Task Adicionar(PagamentoRecord record, CancellationToken ct = default);

    Task Atualizar(PagamentoRecord record, CancellationToken ct = default);

    Task<PagamentoRecord?> ObterPorOrdemServicoEOrcamento(Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default);

    Task<IReadOnlyList<PagamentoRecord>> ListarPorOrdemServico(Guid ordemServicoId, CancellationToken ct = default);
}
