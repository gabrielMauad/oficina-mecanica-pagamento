using MassTransit;
using SharedKernel.Application;
using SharedKernel.Domain;

namespace Pagamento.Infrastructure.Messaging;

/// <summary>
/// Adapter de <see cref="IIntegrationEventBus"/> sobre o MassTransit: publica o evento de integração
/// no tópico do seu tipo (SNS) para que os serviços assinantes o recebam em suas filas (SQS).
/// O tipo da mensagem é o tipo concreto do evento em execução (<c>evento.GetType()</c>), e não o
/// parâmetro genérico: publicar por meio de um tipo abstrato (ex.: <see cref="IIntegrationEvent"/>)
/// continua roteando para o tópico do evento concreto.
/// </summary>
public sealed class MassTransitIntegrationEventBus : IIntegrationEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitIntegrationEventBus(IPublishEndpoint publishEndpoint) =>
        _publishEndpoint = publishEndpoint;

    public Task Publish<T>(T evento, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(evento);
        return _publishEndpoint.Publish(evento, evento.GetType(), cancellationToken);
    }
}
