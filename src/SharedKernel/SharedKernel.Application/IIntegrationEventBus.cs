using SharedKernel.Domain;

namespace SharedKernel.Application;

public interface IIntegrationEventBus
{
    Task Publish<T>(T evento, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent;
}
