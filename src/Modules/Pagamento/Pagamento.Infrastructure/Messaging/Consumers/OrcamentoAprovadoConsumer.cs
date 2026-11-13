using MassTransit;
using OficinaMecanica.Contracts.IntegrationEvents;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;

namespace Pagamento.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer MassTransit de <see cref="OrcamentoAprovadoIntegrationEvent"/>: apenas delega ao handler
/// da Application (<c>CriarPagamentoQuandoOrcamentoAprovado</c>).
/// </summary>
public sealed class OrcamentoAprovadoConsumer : IConsumer<OrcamentoAprovadoIntegrationEvent>
{
    private readonly IIntegrationEventHandler<OrcamentoAprovadoIntegrationEvent> _handler;
    private readonly IntegracoesMetrics _metrics;

    public OrcamentoAprovadoConsumer(
        IIntegrationEventHandler<OrcamentoAprovadoIntegrationEvent> handler,
        IntegracoesMetrics metrics)
    {
        _handler = handler;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<OrcamentoAprovadoIntegrationEvent> context)
    {
        try
        {
            await _handler.Handle(context.Message, context.CancellationToken);
        }
        catch (Exception)
        {
            _metrics.RegistrarFalha(nameof(OrcamentoAprovadoIntegrationEvent), _handler.GetType().Name);
            throw;
        }
    }
}
