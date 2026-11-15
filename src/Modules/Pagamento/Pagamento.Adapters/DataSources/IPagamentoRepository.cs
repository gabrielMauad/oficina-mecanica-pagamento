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

    /// <summary>
    /// Registra que o orçamento foi cancelado antes de existir pagamento dele (o cancelamento chegou
    /// antes da criação). A gravação é condicional à chave do par ainda estar livre, a mesma condição de
    /// <see cref="Adicionar"/>: de dois acessos concorrentes, só um ocupa a chave. Devolve <c>false</c>
    /// (sem gravar) quando a chave já está ocupada por um pagamento ou por outro marcador.
    /// </summary>
    Task<bool> RegistrarCancelamento(
        Guid ordemServicoId, Guid orcamentoId, string motivo, DateTime registradoEm, CancellationToken ct = default);

    /// <summary>Motivo do cancelamento registrado para o par, ou <c>null</c> se não há marcador.</summary>
    Task<string?> ObterMotivoCancelamentoRegistrado(Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default);

    /// <summary>
    /// Troca o marcador de cancelamento do par pelo pagamento (já cancelado). Condicional à existência do
    /// marcador; devolve <c>false</c> (sem gravar) se a chave não tem mais marcador.
    /// </summary>
    Task<bool> SubstituirCancelamentoRegistrado(PagamentoRecord record, CancellationToken ct = default);

    /// <summary>Pagamento do par; o marcador de cancelamento não é um pagamento e devolve <c>null</c>.</summary>
    Task<PagamentoRecord?> ObterPorOrdemServicoEOrcamento(Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default);

    Task<IReadOnlyList<PagamentoRecord>> ListarPorOrdemServico(Guid ordemServicoId, CancellationToken ct = default);
}
