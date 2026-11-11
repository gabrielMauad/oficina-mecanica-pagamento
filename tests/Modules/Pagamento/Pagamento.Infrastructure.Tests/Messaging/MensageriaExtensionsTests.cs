using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Infrastructure.Messaging;
using Pagamento.Infrastructure.Messaging.Consumers;
using SharedKernel.Application;
using SharedKernel.Application.Metrics;
using SharedKernel.Domain;

namespace Pagamento.Infrastructure.Tests.Messaging;

public class MensageriaExtensionsTests
{
    private static IConfiguration Config(params (string Chave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.ToDictionary(v => v.Chave, v => (string?)v.Valor))
            .Build();

    [Fact(DisplayName = "Transporte InMemory — IIntegrationEventBus resolvido é o adapter MassTransit")]
    public async Task IntegrationEventBus_DeveSerOAdapterMassTransit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IntegracoesMetrics>();
        services.AddPagamentoMensageria(Config(("Mensageria:Transporte", "InMemory")));
        await using var provider = services.BuildServiceProvider(true);
        using var scope = provider.CreateScope();

        // Act
        var bus = scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>();

        // Assert
        Assert.IsType<MassTransitIntegrationEventBus>(bus);
    }

    [Fact(DisplayName = "AddSharedKernelServices — não registra o bus in-memory; o adapter MassTransit é o resolvido")]
    public async Task IntegrationEventBus_NaoDeveSerOBusInMemory_ComSharedKernel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPagamentoMensageria(Config(("Mensageria:Transporte", "InMemory")));
        services.AddSharedKernelServices();
        await using var provider = services.BuildServiceProvider(true);
        using var scope = provider.CreateScope();

        // Act
        var bus = scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>();

        // Assert
        Assert.IsType<MassTransitIntegrationEventBus>(bus);
        Assert.IsNotType<InMemoryIntegrationEventBus>(bus);
    }

    [Fact(DisplayName = "Transporte inválido — lança InvalidOperationException")]
    public void TransporteInvalido_DeveLancarExcecao()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPagamentoMensageria(Config(("Mensageria:Transporte", "Kafka"))));

        Assert.Contains("Kafka", ex.Message);
    }

    [Fact(DisplayName = "Sem configuração — o transporte padrão é AmazonSqs na região us-east-1")]
    public void SemConfiguracao_DeveUsarAmazonSqsPadrao()
    {
        var options = new MensageriaOptions();

        Assert.Equal(MensageriaOptions.TransporteAmazonSqs, options.Transporte);
        Assert.Equal("us-east-1", options.Regiao);
    }

    [Fact(DisplayName = "Transporte AmazonSqs — registra o MassTransit sem abrir conexão ao construir o provider")]
    public async Task AmazonSqs_DeveRegistrarSemConectar()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IntegracoesMetrics>();
        services.AddPagamentoMensageria(Config(
            ("Mensageria:Transporte", "AmazonSqs"),
            ("Mensageria:Regiao", "us-east-1"),
            ("Mensageria:ServiceUrl", "http://localhost:4566"),
            ("Mensageria:AccessKey", "test"),
            ("Mensageria:SecretKey", "test")));

        // Act
        await using var provider = services.BuildServiceProvider(true);
        using var scope = provider.CreateScope();

        // Assert — o bus só é iniciado pelo hosted service; aqui apenas o registro é verificado
        Assert.NotNull(scope.ServiceProvider.GetService<IPublishEndpoint>());
    }

    [Fact(DisplayName = "MassTransitIntegrationEventBus — publica o evento no IPublishEndpoint")]
    public async Task Bus_DevePublicarNoPublishEndpoint()
    {
        // Arrange
        var publishEndpoint = new Mock<IPublishEndpoint>();
        var bus = new MassTransitIntegrationEventBus(publishEndpoint.Object);
        var evento = new PagamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Act
        await bus.Publish(evento);

        // Assert
        publishEndpoint.Verify(p => p.Publish(
            (object)evento, typeof(PagamentoAprovadoIntegrationEvent), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "MassTransitIntegrationEventBus — publicar via tipo abstrato roteia pelo tipo concreto do evento")]
    public async Task Bus_PublicandoViaTipoAbstrato_DeveUsarTipoConcreto()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMassTransitTestHarness();
        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var bus = new MassTransitIntegrationEventBus(harness.Bus);
        IIntegrationEvent evento = new PagamentoRecusadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "motivo");

        // Act
        await bus.Publish(evento);

        // Assert
        Assert.True(await harness.Published.Any<PagamentoRecusadoIntegrationEvent>(
            m => m.Context.Message.EventId == evento.EventId));
    }

    [Fact(DisplayName = "MassTransitIntegrationEventBus — evento nulo lança ArgumentNullException")]
    public async Task Bus_EventoNulo_DeveLancarArgumentNullException()
    {
        var bus = new MassTransitIntegrationEventBus(new Mock<IPublishEndpoint>().Object);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => bus.Publish<PagamentoAprovadoIntegrationEvent>(null!));
    }

    [Fact(DisplayName = "Fila do serviço — o consumer de OrcamentoAprovado tem o prefixo pagamento")]
    public void Consumer_DeveTerFilaComPrefixoDoServico()
    {
        var formatter = new KebabCaseEndpointNameFormatter("pagamento", false);

        Assert.Equal("pagamento-orcamento-aprovado", formatter.Consumer<OrcamentoAprovadoConsumer>());
    }
}
