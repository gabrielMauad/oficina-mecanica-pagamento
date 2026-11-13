namespace Pagamento.Domain;

/// <summary>
/// Ciclo de status do pagamento, interno ao serviço de Pagamento: nasce <see cref="Pendente"/> e
/// termina em <see cref="Aprovado"/>, <see cref="Recusado"/> ou <see cref="Cancelado"/>, sem volta.
/// </summary>
public enum StatusPagamento
{
    Pendente,
    Aprovado,
    Recusado,

    /// <summary>Cancelado antes de ser pago: o orçamento deixou de valer (a baixa de estoque falhou).</summary>
    Cancelado
}
