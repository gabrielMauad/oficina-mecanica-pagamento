using SharedKernel.Domain;

namespace SharedKernel.Application;

public interface IIntegrationEventHandler<T>
    where T : IIntegrationEvent
{
    Task Handle(T integrationEvent, CancellationToken cancellationToken = default);
}
