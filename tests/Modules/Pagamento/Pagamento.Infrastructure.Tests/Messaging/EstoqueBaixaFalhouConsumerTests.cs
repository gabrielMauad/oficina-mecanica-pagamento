using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Infrastructure.Messaging.Consumers;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;

namespace Pagamento.Infrastructure.Tests.Messaging;

public class EstoqueBaixaFalhouConsumerTests
{
    private static EstoqueBaixaFalhouIntegrationEvent NovoEvento() =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "Estoque insuficiente");

    private static async Task<(ServiceProvider Provider, ITestHarness Harness)> IniciarAsync(
        IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IntegracoesMetrics>();
        services.AddSingleton(handler);
        services.AddMassTransitTestHarness(bus => bus.AddConsumer<EstoqueBaixaFalhouConsumer>());
        var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (provider, harness);
    }

    [Fact(DisplayName = "Consumer — delega a mensagem ao handler da Application")]
    public async Task Consumer_DeveDelegarAoHandler()
    {
        // Arrange
        var handler = new Mock<IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>>();
        var (provider, harness) = await IniciarAsync(handler.Object);
        await using var _ = provider;
        var evento = NovoEvento();

        // Act
        await harness.Bus.Publish(evento);

        // Assert
        Assert.True(await harness.Consumed.Any<EstoqueBaixaFalhouIntegrationEvent>(m => m.Context.Message.EventId == evento.EventId));
        handler.Verify(h => h.Handle(
            It.Is<EstoqueBaixaFalhouIntegrationEvent>(e => e.EventId == evento.EventId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Consumer — falha do handler é propagada ao MassTransit (para o retry)")]
    public async Task Consumer_FalhaDoHandler_DevePropagar()
    {
        // Arrange
        var handler = new Mock<IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>>();
        handler
            .Setup(h => h.Handle(It.IsAny<EstoqueBaixaFalhouIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha técnica"));
        var (provider, harness) = await IniciarAsync(handler.Object);
        await using var _ = provider;
        var consumerHarness = harness.GetConsumerHarness<EstoqueBaixaFalhouConsumer>();

        // Act
        await harness.Bus.Publish(NovoEvento());

        // Assert
        Assert.True(await consumerHarness.Consumed.Any<EstoqueBaixaFalhouIntegrationEvent>());
        Assert.True(await harness.Published.Any<Fault<EstoqueBaixaFalhouIntegrationEvent>>());
    }

    [Fact(DisplayName = "Fila do serviço — o consumer de EstoqueBaixaFalhou tem o prefixo pagamento")]
    public void Consumer_DeveTerFilaComPrefixoDoServico()
    {
        var formatter = new KebabCaseEndpointNameFormatter("pagamento", false);

        Assert.Equal("pagamento-estoque-baixa-falhou", formatter.Consumer<EstoqueBaixaFalhouConsumer>());
    }
}
