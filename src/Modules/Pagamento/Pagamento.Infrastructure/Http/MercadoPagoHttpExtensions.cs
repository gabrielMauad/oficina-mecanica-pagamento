using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pagamento.Adapters.Gateways;
using Pagamento.Application.Gateways;

namespace Pagamento.Infrastructure.Http;

public static class MercadoPagoHttpExtensions
{
    /// <summary>
    /// Registra o processador de pagamento como <see cref="HttpClient"/> tipado para a URL base do
    /// Mercado Pago (<see cref="MercadoPagoOptions.BaseUrl"/>), lida das opções quando o cliente é
    /// criado. As opções são validadas na partida da aplicação.
    /// </summary>
    public static IServiceCollection AddProcessadorPagamentoMercadoPago(this IServiceCollection services)
    {
        services.AddOptions<MercadoPagoOptions>()
            .BindConfiguration(MercadoPagoOptions.SectionName)
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "MercadoPago:BaseUrl não configurado.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "MercadoPago:ClientId não configurado.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "MercadoPago:ClientSecret não configurado.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MercadoPagoOptions>>().Value;
            return new MercadoPagoCredenciais(options.ClientId!, options.ClientSecret!);
        });

        services.AddHttpClient<IProcessadorPagamentoGateway, MercadoPagoGateway>((sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<IOptions<MercadoPagoOptions>>().Value.BaseUrl!;
            // A barra final garante que caminhos relativos ("oauth/token") sejam anexados à URL base.
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        });

        return services;
    }
}
