namespace Pagamento.Domain;

/// <summary>
/// Ciclo de status do pagamento, interno ao serviço de Pagamento: nasce <see cref="Pendente"/> e
/// termina em <see cref="Aprovado"/> ou <see cref="Recusado"/>, sem volta.
/// </summary>
public enum StatusPagamento
{
    Pendente,
    Aprovado,
    Recusado
}
