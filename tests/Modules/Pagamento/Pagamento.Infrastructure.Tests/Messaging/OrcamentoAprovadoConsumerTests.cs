using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Infrastructure.Messaging.Consumers;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;

namespace Pagamento.Infrastructure.Tests.Messaging;

public class OrcamentoAprovadoConsumerTests
{
    private static OrcamentoAprovadoIntegrationEvent NovoEvento() =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 420m);

    private static async Task<(ServiceProvider Provider, ITestHarness Harness)> IniciarAsync(
        IIntegrationEventHandler<OrcamentoAprovadoIntegrationEvent> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IntegracoesMetrics>();
        services.AddSingleton(handler);
        services.AddMassTransitTestHarness(bus => bus.AddConsumer<OrcamentoAprovadoConsumer>());
        var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (provider, harness);
    }

    [Fact(DisplayName = "Consumer — delega a mensagem ao handler da Application")]
    public async Task Consumer_DeveDelegarAoHandler()
    {
        // Arrange
        var handler = new Mock<IIntegrationEventHandler<OrcamentoAprovadoIntegrationEvent>>();
        var (provider, harness) = await IniciarAsync(handler.Object);
        await using var _ = provider;
        var evento = NovoEvento();

        // Act
        await harness.Bus.Publish(evento);

        // Assert
        Assert.True(await harness.Consumed.Any<OrcamentoAprovadoIntegrationEvent>(m => m.Context.Message.EventId == evento.EventId));
        handler.Verify(h => h.Handle(
            It.Is<OrcamentoAprovadoIntegrationEvent>(e => e.EventId == evento.EventId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Consumer — falha do handler é propagada ao MassTransit (para o retry)")]
    public async Task Consumer_FalhaDoHandler_DevePropagar()
    {
        // Arrange
        var handler = new Mock<IIntegrationEventHandler<OrcamentoAprovadoIntegrationEvent>>();
        handler
            .Setup(h => h.Handle(It.IsAny<OrcamentoAprovadoIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha técnica"));
        var (provider, harness) = await IniciarAsync(handler.Object);
        await using var _ = provider;
        var consumerHarness = harness.GetConsumerHarness<OrcamentoAprovadoConsumer>();

        // Act
        await harness.Bus.Publish(NovoEvento());

        // Assert
        Assert.True(await consumerHarness.Consumed.Any<OrcamentoAprovadoIntegrationEvent>());
        Assert.True(await harness.Published.Any<Fault<OrcamentoAprovadoIntegrationEvent>>());
    }
}
