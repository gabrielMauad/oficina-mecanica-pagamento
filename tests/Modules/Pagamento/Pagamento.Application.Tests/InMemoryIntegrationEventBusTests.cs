using System.Diagnostics.Metrics;
using Moq;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;
using SharedKernel.Domain;

namespace Pagamento.Application.Tests;

public class InMemoryIntegrationEventBusTests
{
    public sealed record EventoFake(Guid EventId, DateTime OcorridoEm) : IIntegrationEvent;

    private static EventoFake NovoEvento() => new(Guid.NewGuid(), DateTime.UtcNow);

    private static IServiceProvider ProviderCom(params IIntegrationEventHandler<EventoFake>[] handlers)
    {
        var provider = new Mock<IServiceProvider>();
        provider
            .Setup(p => p.GetService(typeof(IEnumerable<IIntegrationEventHandler<EventoFake>>)))
            .Returns(handlers);
        return provider.Object;
    }

    [Fact(DisplayName = "Publish — entrega o evento a todos os handlers registrados, em ordem")]
    public async Task Publish_DeveChamarTodosOsHandlers()
    {
        var ordem = new List<string>();
        var h1 = new Mock<IIntegrationEventHandler<EventoFake>>();
        var h2 = new Mock<IIntegrationEventHandler<EventoFake>>();
        h1.Setup(h => h.Handle(It.IsAny<EventoFake>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask).Callback(() => ordem.Add("h1"));
        h2.Setup(h => h.Handle(It.IsAny<EventoFake>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask).Callback(() => ordem.Add("h2"));
        using var metrics = new IntegracoesMetrics();
        var bus = new InMemoryIntegrationEventBus(ProviderCom(h1.Object, h2.Object), metrics);
        var evento = NovoEvento();

        await bus.Publish(evento, TestContext.Current.CancellationToken);

        Assert.Equal(["h1", "h2"], ordem);
        h1.Verify(h => h.Handle(evento, It.IsAny<CancellationToken>()), Times.Once);
        h2.Verify(h => h.Handle(evento, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Publish — sem handlers registrados não falha nem registra métrica de falha")]
    public async Task Publish_SemHandlers_NaoDeveFalhar()
    {
        using var metrics = new IntegracoesMetrics();
        var bus = new InMemoryIntegrationEventBus(ProviderCom(), metrics);

        var falhasRegistradas = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrumento, l) =>
        {
            if (instrumento.Meter == metrics.Meter && instrumento.Name == IntegracoesMetrics.FalhasInstrumentName)
                l.EnableMeasurementEvents(instrumento);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => falhasRegistradas++);
        listener.Start();

        var excecao = await Record.ExceptionAsync(
            () => bus.Publish(NovoEvento(), TestContext.Current.CancellationToken));

        Assert.Null(excecao);
        Assert.Equal(0, falhasRegistradas);
    }

    [Fact(DisplayName = "Publish — handler que falha registra a métrica de falha e propaga a exceção")]
    public async Task Publish_HandlerFalha_DeveRegistrarMetricaEPropagar()
    {
        var falha = new InvalidOperationException("boom");
        var handler = new Mock<IIntegrationEventHandler<EventoFake>>();
        handler.Setup(h => h.Handle(It.IsAny<EventoFake>(), It.IsAny<CancellationToken>())).ThrowsAsync(falha);
        using var metrics = new IntegracoesMetrics();
        var bus = new InMemoryIntegrationEventBus(ProviderCom(handler.Object), metrics);

        var medicoes = new List<(long Valor, string? Evento)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrumento, l) =>
        {
            if (instrumento.Meter == metrics.Meter && instrumento.Name == IntegracoesMetrics.FalhasInstrumentName)
                l.EnableMeasurementEvents(instrumento);
        };
        listener.SetMeasurementEventCallback<long>((_, valor, tags, _) =>
        {
            var evento = tags.ToArray().FirstOrDefault(t => t.Key == "evento").Value as string;
            medicoes.Add((valor, evento));
        });
        listener.Start();

        var lancada = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bus.Publish(NovoEvento(), TestContext.Current.CancellationToken));

        Assert.Same(falha, lancada);
        Assert.Single(medicoes);
        Assert.Equal((1L, nameof(EventoFake)), medicoes[0]);
    }
}
