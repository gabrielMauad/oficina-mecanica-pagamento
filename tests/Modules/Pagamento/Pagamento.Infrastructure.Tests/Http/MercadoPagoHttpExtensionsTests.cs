using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pagamento.Adapters.Gateways;
using Pagamento.Application.Gateways;
using Pagamento.Infrastructure.Http;

namespace Pagamento.Infrastructure.Tests.Http;

public class MercadoPagoHttpExtensionsTests
{
    private static ServiceProvider Provider(params (string Chave, string? Valor)[] valores)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(valores.ToDictionary(v => v.Chave, v => v.Valor))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddProcessadorPagamentoMercadoPago();
        return services.BuildServiceProvider(true);
    }

    [Fact(DisplayName = "Configuração completa — o processador é o MercadoPagoGateway")]
    public void ConfiguracaoCompleta_DeveResolverMercadoPagoGateway()
    {
        using var provider = Provider(
            ("MercadoPago:BaseUrl", "http://localhost:8080/mock/mercadopago"),
            ("MercadoPago:ClientId", "mp-dev-client"),
            ("MercadoPago:ClientSecret", "mp-dev-secret"));
        using var scope = provider.CreateScope();

        var processador = scope.ServiceProvider.GetRequiredService<IProcessadorPagamentoGateway>();

        Assert.IsType<MercadoPagoGateway>(processador);
        Assert.Equal(
            new MercadoPagoCredenciais("mp-dev-client", "mp-dev-secret"),
            provider.GetRequiredService<MercadoPagoCredenciais>());
    }

    [Theory(DisplayName = "Barra final da URL base é normalizada para os caminhos relativos funcionarem")]
    [InlineData("http://localhost:8080/mock/mercadopago")]
    [InlineData("http://localhost:8080/mock/mercadopago/")]
    public void BaseUrl_DeveTerBarraFinalUnica(string baseUrl)
    {
        using var provider = Provider(
            ("MercadoPago:BaseUrl", baseUrl),
            ("MercadoPago:ClientId", "id"),
            ("MercadoPago:ClientSecret", "segredo"));

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var cliente = factory.CreateClient(nameof(IProcessadorPagamentoGateway));

        Assert.Equal(new Uri("http://localhost:8080/mock/mercadopago/"), cliente.BaseAddress);
        Assert.Equal(
            "http://localhost:8080/mock/mercadopago/oauth/token",
            new Uri(cliente.BaseAddress!, "oauth/token").ToString());
    }

    [Theory(DisplayName = "Configuração incompleta — a validação das opções falha")]
    [InlineData(null, "id", "segredo", "MercadoPago:BaseUrl")]
    [InlineData("http://localhost", null, "segredo", "MercadoPago:ClientId")]
    [InlineData("http://localhost", "id", " ", "MercadoPago:ClientSecret")]
    public void ConfiguracaoIncompleta_DeveFalharNaValidacao(string? baseUrl, string? clientId, string? clientSecret, string chaveEsperada)
    {
        using var provider = Provider(
            ("MercadoPago:BaseUrl", baseUrl),
            ("MercadoPago:ClientId", clientId),
            ("MercadoPago:ClientSecret", clientSecret));

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MercadoPagoOptions>>().Value);

        Assert.Contains(chaveEsperada, ex.Message);
    }
}
