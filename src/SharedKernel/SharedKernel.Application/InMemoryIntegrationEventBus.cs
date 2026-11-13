using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Metrics;
using SharedKernel.Domain;

namespace SharedKernel.Application;

public sealed class InMemoryIntegrationEventBus : IIntegrationEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IntegracoesMetrics _metrics;

    public InMemoryIntegrationEventBus(IServiceProvider serviceProvider, IntegracoesMetrics metrics)
    {
        _serviceProvider = serviceProvider;
        _metrics = metrics;
    }

    public async Task Publish<T>(T evento, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent
    {
        var handlers = _serviceProvider.GetServices<IIntegrationEventHandler<T>>();

        foreach (var handler in handlers)
        {
            try
            {
                await handler.Handle(evento, cancellationToken);
            }
            catch (Exception)
            {
                _metrics.RegistrarFalha(typeof(T).Name, handler.GetType().Name);
                throw;
            }
        }
    }
}
