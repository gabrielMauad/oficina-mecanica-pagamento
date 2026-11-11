using MediatR;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Domain.Events;
using SharedKernel.Application;

namespace Pagamento.Application.DomainEventHandlers;

public sealed class PublicarPagamentoRecusado : INotificationHandler<PagamentoRecusado>
{
    private readonly IIntegrationEventBus _bus;
    private readonly IPendingIntegrationEvents _pendingEvents;

    public PublicarPagamentoRecusado(IIntegrationEventBus bus, IPendingIntegrationEvents pendingEvents)
    {
        _bus = bus;
        _pendingEvents = pendingEvents;
    }

    public Task Handle(PagamentoRecusado notification, CancellationToken ct)
    {
        PagamentoRecusadoIntegrationEvent integrationEvent = new(
            Guid.NewGuid(),
            DateTime.UtcNow,
            notification.PagamentoId.Value,
            notification.OrdemServicoId,
            notification.OrcamentoId,
            notification.Motivo);
        _pendingEvents.Enqueue(ct => _bus.Publish(integrationEvent, ct));
        return Task.CompletedTask;
    }
}
