using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace IntegrationTests.MercadoPagoMock;

/// <summary>
/// Mock do Mercado Pago exposto pelo host: troca credenciais de cliente por um token e cria pagamentos
/// com esse token, sem exigir o JWT da oficina.
/// </summary>
[Collection("Integration")]
public class MercadoPagoMockTests(PagamentoWebApplicationFactory factory)
{
    private const string RotaToken = "/mock/mercadopago/oauth/token";
    private const string RotaPagamentos = "/mock/mercadopago/v1/payments";

    private static object Credenciais(string? clientId = null, string? clientSecret = null, string grantType = "client_credentials") =>
        new
        {
            client_id = clientId ?? PagamentoWebApplicationFactory.MercadoPagoClientId,
            client_secret = clientSecret ?? PagamentoWebApplicationFactory.MercadoPagoClientSecret,
            grant_type = grantType
        };

    private static async Task<string> ObterTokenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(RotaToken, Credenciais());
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<HttpResponseMessage> CriarPagamentoAsync(
        HttpClient client, string? token, string titular, decimal valor = 150m)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RotaPagamentos)
        {
            Content = JsonContent.Create(new
            {
                transaction_amount = valor,
                external_reference = Guid.NewGuid().ToString(),
                description = "Pagamento de teste",
                card_holder_name = titular
            })
        };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    [Fact(DisplayName = "Token — credenciais corretas devolvem 200 com access_token bearer de 6 horas, sem JWT da oficina")]
    public async Task Token_CredenciaisCorretas_DeveEmitirToken()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(RotaToken, Credenciais());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("access_token").GetString()));
        Assert.Equal("bearer", json.RootElement.GetProperty("token_type").GetString());
        Assert.Equal(21600, json.RootElement.GetProperty("expires_in").GetInt32());
    }

    [Theory(DisplayName = "Token — credenciais diferentes das configuradas devolvem 401")]
    [InlineData("outro-client", null)]
    [InlineData(null, "outro-secret")]
    public async Task Token_CredenciaisIncorretas_DeveRetornar401(string? clientId, string? clientSecret)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(RotaToken, Credenciais(clientId, clientSecret));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Token — grant_type diferente de client_credentials devolve 400")]
    public async Task Token_GrantTypeInvalido_DeveRetornar400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(RotaToken, Credenciais(grantType: "password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "Pagamento — titular APRO devolve 201 approved/accredited com id numérico")]
    public async Task Pagamento_Apro_DeveAprovar()
    {
        using var client = factory.CreateClient();
        var token = await ObterTokenAsync(client);

        var response = await CriarPagamentoAsync(client, token, "APRO");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("id").GetInt64() > 0);
        Assert.Equal("approved", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("accredited", json.RootElement.GetProperty("status_detail").GetString());
    }

    [Theory(DisplayName = "Pagamento — qualquer outro titular devolve 201 rejected/cc_rejected_other_reason")]
    [InlineData("OTHE")]
    [InlineData("apro")]
    [InlineData("")]
    public async Task Pagamento_OutroTitular_DeveRecusar(string titular)
    {
        using var client = factory.CreateClient();
        var token = await ObterTokenAsync(client);

        var response = await CriarPagamentoAsync(client, token, titular);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("rejected", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("cc_rejected_other_reason", json.RootElement.GetProperty("status_detail").GetString());
    }

    [Fact(DisplayName = "Pagamento — sem Bearer devolve 401")]
    public async Task Pagamento_SemBearer_DeveRetornar401()
    {
        using var client = factory.CreateClient();

        var response = await CriarPagamentoAsync(client, token: null, "APRO");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(DisplayName = "Pagamento — token inválido devolve 401")]
    [InlineData("qualquer-coisa")]
    [InlineData("APP_USR-9999999999-ABCDEF")]
    [InlineData("APP_USR-1-ABCDEF")]
    public async Task Pagamento_TokenInvalido_DeveRetornar401(string token)
    {
        using var client = factory.CreateClient();

        var response = await CriarPagamentoAsync(client, token, "APRO");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Pagamento — o JWT da oficina não vale como token do mock")]
    public async Task Pagamento_JwtDaOficina_DeveRetornar401()
    {
        using var oficina = factory.CreateOficinaClient();
        var jwt = oficina.DefaultRequestHeaders.Authorization!.Parameter;

        var response = await CriarPagamentoAsync(factory.CreateClient(), jwt, "APRO");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Pagamento — valor zero devolve 400")]
    public async Task Pagamento_ValorZero_DeveRetornar400()
    {
        using var client = factory.CreateClient();
        var token = await ObterTokenAsync(client);

        var response = await CriarPagamentoAsync(client, token, "APRO", valor: 0m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "Réplicas — token emitido por uma instância vale em outra com as mesmas credenciais")]
    public async Task Token_EmitidoEmUmaReplica_ValeEmOutra()
    {
        // Arrange — segunda instância do serviço, com as mesmas credenciais do mock
        await using var outraReplica = factory.WithWebHostBuilder(_ => { });
        using var primeira = factory.CreateClient();
        using var segunda = outraReplica.CreateClient();
        var token = await ObterTokenAsync(primeira);

        // Act
        var response = await CriarPagamentoAsync(segunda, token, "APRO");

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact(DisplayName = "Réplicas — instância com outro client_secret recusa o token")]
    public async Task Token_EmOutraInstanciaComOutroSegredo_DeveSerRecusado()
    {
        // Arrange
        await using var outroSegredo = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["MercadoPago:ClientSecret"] = "segredo-diferente" })));
        using var primeira = factory.CreateClient();
        using var segunda = outroSegredo.CreateClient();
        var token = await ObterTokenAsync(primeira);

        // Act
        var response = await CriarPagamentoAsync(segunda, token, "APRO");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
