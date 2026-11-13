using Amazon.SimpleNotificationService;
using Amazon.SQS;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pagamento.Infrastructure.Messaging.Consumers;
using SharedKernel.Application;

namespace Pagamento.Infrastructure.Messaging;

public static class MensageriaExtensions
{
    private const string PrefixoDasFilas = "pagamento";

    /// <summary>
    /// Registra o MassTransit (Amazon SQS/SNS, ou InMemory conforme <c>Mensageria__Transporte</c>), os
    /// consumers do serviço e o <see cref="IIntegrationEventBus"/> baseado em <c>IPublishEndpoint</c>.
    /// </summary>
    public static IServiceCollection AddPagamentoMensageria(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(MensageriaOptions.SectionName).Get<MensageriaOptions>()
            ?? new MensageriaOptions();

        services.AddScoped<IIntegrationEventBus, MassTransitIntegrationEventBus>();

        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(PrefixoDasFilas, false));

            bus.AddConsumer<OrcamentoAprovadoConsumer>();
            bus.AddConsumer<EstoqueBaixaFalhouConsumer>();

            if (string.Equals(options.Transporte, MensageriaOptions.TransporteInMemory, StringComparison.OrdinalIgnoreCase))
            {
                bus.UsingInMemory((context, cfg) =>
                {
                    ConfigurarRetry(cfg);
                    cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter(PrefixoDasFilas, false));
                });
            }
            else if (string.Equals(options.Transporte, MensageriaOptions.TransporteAmazonSqs, StringComparison.OrdinalIgnoreCase))
            {
                bus.UsingAmazonSqs((context, cfg) =>
                {
                    cfg.Host(options.Regiao, host =>
                    {
                        if (!string.IsNullOrWhiteSpace(options.AccessKey) && !string.IsNullOrWhiteSpace(options.SecretKey))
                        {
                            host.AccessKey(options.AccessKey);
                            host.SecretKey(options.SecretKey);
                        }

                        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
                        {
                            host.Config(new AmazonSQSConfig { ServiceURL = options.ServiceUrl });
                            host.Config(new AmazonSimpleNotificationServiceConfig { ServiceURL = options.ServiceUrl });
                        }
                    });

                    ConfigurarRetry(cfg);
                    cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter(PrefixoDasFilas, false));
                });
            }
            else
            {
                throw new InvalidOperationException(
                    $"Mensageria:Transporte inválido: '{options.Transporte}'. Valores aceitos: " +
                    $"{MensageriaOptions.TransporteAmazonSqs}, {MensageriaOptions.TransporteInMemory}.");
            }
        });

        return services;
    }

    private static void ConfigurarRetry(IBusFactoryConfigurator configurator) =>
        configurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10)));
}
