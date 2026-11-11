using MediatR;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Domain.Events;
using SharedKernel.Application;

namespace Pagamento.Application.DomainEventHandlers;

public sealed class PublicarPagamentoAprovado : INotificationHandler<PagamentoAprovado>
{
    private readonly IIntegrationEventBus _bus;
    private readonly IPendingIntegrationEvents _pendingEvents;

    public PublicarPagamentoAprovado(IIntegrationEventBus bus, IPendingIntegrationEvents pendingEvents)
    {
        _bus = bus;
        _pendingEvents = pendingEvents;
    }

    public Task Handle(PagamentoAprovado notification, CancellationToken ct)
    {
        PagamentoAprovadoIntegrationEvent integrationEvent = new(
            Guid.NewGuid(),
            DateTime.UtcNow,
            notification.PagamentoId.Value,
            notification.OrdemServicoId,
            notification.OrcamentoId);
        _pendingEvents.Enqueue(ct => _bus.Publish(integrationEvent, ct));
        return Task.CompletedTask;
    }
}
