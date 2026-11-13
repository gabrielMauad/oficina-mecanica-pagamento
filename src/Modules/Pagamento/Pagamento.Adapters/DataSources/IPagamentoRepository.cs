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

    /// <summary>
    /// Grava o novo estado do pagamento só se o item persistido ainda estiver <c>Pendente</c>.
    /// Devolve <c>false</c> (sem gravar) quando outro processo já resolveu o pagamento.
    /// </summary>
    Task<bool> AtualizarSePendente(PagamentoRecord record, CancellationToken ct = default);

    Task<PagamentoRecord?> ObterPorOrdemServicoEOrcamento(Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default);

    Task<IReadOnlyList<PagamentoRecord>> ListarPorOrdemServico(Guid ordemServicoId, CancellationToken ct = default);
}
