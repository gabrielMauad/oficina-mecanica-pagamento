using Amazon;
using Amazon.DynamoDBv2;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Adapters.Controllers;
using Pagamento.Adapters.DataSources;
using Pagamento.Adapters.Gateways;
using Pagamento.Application.Commands.CriarPagamento;
using Pagamento.Application.Gateways;
using Pagamento.Application.IntegrationEventHandlers;
using Pagamento.Infrastructure.Http;
using Pagamento.Infrastructure.Messaging;
using Pagamento.Infrastructure.Persistence;
using SharedKernel.Application;

namespace Pagamento.Infrastructure;

public static class PagamentoModule
{
    public static IServiceCollection AddPagamentoModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Este módulo não tem IUnitOfWork: o DynamoDB grava cada item na hora, dentro do handler.
        // O TransactionBehavior itera as unidades de trabalho registradas (nenhuma aqui) e segue
        // publicando os eventos de domínio e de integração depois que o handler termina.

        #region Persistência (DynamoDB)

        services.AddOptions<DynamoDbOptions>()
            .BindConfiguration(DynamoDbOptions.SectionName)
            .Validate(o => !string.IsNullOrWhiteSpace(o.TabelaPagamentos), "DynamoDb:TabelaPagamentos não configurado.")
            .ValidateOnStart();

        services.AddSingleton<IAmazonDynamoDB>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DynamoDbOptions>>().Value;
            var config = new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(options.Regiao) };

            // Só local: aponta para o LocalStack/DynamoDB Local. Na AWS fica vazio e o SDK resolve o endpoint
            // da região, com as credenciais da cadeia padrão (role do nó).
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                config.ServiceURL = options.ServiceUrl;
                config.AuthenticationRegion = options.Regiao;
            }

            return new AmazonDynamoDBClient(config);
        });
        services.AddSingleton<TabelaPagamentosInicializador>();
        services.AddScoped<IPagamentoRepository, PagamentoRepository>();

        #endregion

        #region Gateways (Interface Adapters)

        services.AddScoped<IPagamentoGateway, PagamentoGateway>();
        services.AddProcessadorPagamentoMercadoPago();

        #endregion

        #region Controllers CA

        services.AddScoped<PagamentoController>();

        #endregion

        #region Integration Events

        services.AddScoped<IIntegrationEventHandler<OrcamentoAprovadoIntegrationEvent>, CriarPagamentoQuandoOrcamentoAprovado>();

        #endregion

        #region Mensageria (MassTransit — consumers e IIntegrationEventBus)

        services.AddPagamentoMensageria(configuration);

        #endregion

        var applicationAssembly = typeof(CriarPagamentoCommand).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);
            cfg.AddOpenBehavior(typeof(SharedKernel.Application.Behaviors.LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SharedKernel.Application.Behaviors.ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(SharedKernel.Application.Behaviors.TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(applicationAssembly);

        return services;
    }
}
