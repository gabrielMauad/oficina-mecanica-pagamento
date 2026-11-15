using Pagamento.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Gateways;

/// <summary>
/// Port de persistência dos pagamentos. A Application não conhece o banco que os guarda.
/// </summary>
public interface IPagamentoGateway
{
    Task Adicionar(PagamentoEntity pagamento, CancellationToken ct = default);

    /// <summary>
    /// Persiste a saída do pagamento de <see cref="StatusPagamento.Pendente"/> (aprovado, recusado ou
    /// cancelado). A gravação é condicional ao pagamento persistido ainda estar <c>Pendente</c>:
    /// devolve <c>false</c> quando outra chamada concorrente o resolveu antes.
    /// </summary>
    Task<bool> AtualizarSePendente(PagamentoEntity pagamento, CancellationToken ct = default);

    /// <summary>
    /// Registra que o orçamento foi cancelado antes de existir pagamento dele, para que a criação
    /// posterior nasça cancelada. Condicional à chave do pagamento do orçamento estar livre; devolve
    /// <c>false</c> quando já existe pagamento (ou registro) dele.
    /// </summary>
    Task<bool> RegistrarCancelamento(Guid ordemServicoId, Guid orcamentoId, string motivo, CancellationToken ct = default);

    /// <summary>Motivo do cancelamento registrado antes da criação do pagamento do orçamento, ou <c>null</c>.</summary>
    Task<string?> ObterMotivoCancelamentoRegistrado(Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default);

    /// <summary>
    /// Grava o pagamento (já cancelado) no lugar do registro de cancelamento do orçamento. Devolve
    /// <c>false</c> quando o registro não existe mais (outra entrega do mesmo evento já o substituiu).
    /// </summary>
    Task<bool> SubstituirCancelamentoRegistrado(PagamentoEntity pagamento, CancellationToken ct = default);

    Task<PagamentoEntity?> ObterPorOrdemServicoEOrcamento(Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default);

    /// <summary>Pagamento ainda <see cref="StatusPagamento.Pendente"/> da ordem de serviço, ou <c>null</c>.</summary>
    Task<PagamentoEntity?> ObterPendentePorOrdemServico(Guid ordemServicoId, CancellationToken ct = default);

    Task<IReadOnlyList<PagamentoEntity>> ListarPorOrdemServico(Guid ordemServicoId, CancellationToken ct = default);
}
