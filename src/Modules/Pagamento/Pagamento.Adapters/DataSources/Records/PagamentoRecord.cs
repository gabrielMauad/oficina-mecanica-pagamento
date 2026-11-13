namespace Pagamento.Adapters.DataSources.Records;

/// <summary>
/// Representação do pagamento na camada de persistência, sem regra de negócio. O status é guardado
/// como texto (<c>Pendente</c>, <c>Aprovado</c>, <c>Recusado</c> ou <c>Cancelado</c>).
/// </summary>
public sealed record PagamentoRecord(
    Guid PagamentoId,
    Guid OrdemServicoId,
    Guid OrcamentoId,
    Guid ClienteId,
    decimal Valor,
    string Status,
    string? MotivoRecusa,
    long? MercadoPagoPaymentId,
    DateTime CriadoEm,
    DateTime AtualizadoEm);
