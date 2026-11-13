using SharedKernel.Domain;

namespace Pagamento.Domain.Events;

public sealed record PagamentoAprovado(
    PagamentoId PagamentoId,
    Guid OrdemServicoId,
    Guid OrcamentoId,
    DateTime OcorridoEm
) : IDomainEvent;
