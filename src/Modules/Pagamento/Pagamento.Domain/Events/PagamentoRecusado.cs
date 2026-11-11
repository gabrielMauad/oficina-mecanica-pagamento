using SharedKernel.Domain;

namespace Pagamento.Domain.Events;

public sealed record PagamentoRecusado(
    PagamentoId PagamentoId,
    Guid OrdemServicoId,
    Guid OrcamentoId,
    string Motivo,
    DateTime OcorridoEm
) : IDomainEvent;
