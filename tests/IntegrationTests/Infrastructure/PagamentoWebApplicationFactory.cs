using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.DynamoDb;

namespace IntegrationTests.Infrastructure;

/// <summary>
/// Factory compartilhada por todos os testes de integração via [Collection("Integration")].
/// Sobe um DynamoDB real (DynamoDB Local, via Testcontainers) uma única vez por sessão de testes; o
/// próprio serviço cria a tabela na partida (<c>DynamoDb:CriarTabelaSeNaoExistir</c>). Usa o transporte
/// InMemory do MassTransit com o test harness e roteia as chamadas do adapter do Mercado Pago para o
/// mock do próprio host de teste, como acontece em produção (a URL base aponta para o serviço).
/// </summary>
public sealed class PagamentoWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TabelaPagamentos = "pagamentos-testes";
    public const string MercadoPagoClientId = "mp-test-client";
    public const string MercadoPagoClientSecret = "mp-test-secret";

    private const string TestJwtSecret = "integration-test-jwt-secret-minimum-32-chars!!";
    private const string AppIssuer = "oficina-mecanica-app";
    private const string ClienteAuthIssuer = "oficina-mecanica-auth";
    private const string Audience = "oficina-mecanica-api";

    private readonly DynamoDbContainer _dynamoDb = new DynamoDbBuilder("amazon/dynamodb-local:2.5.2").Build();

    static PagamentoWebApplicationFactory()
    {
        // O serviço usa a cadeia padrão de credenciais do SDK; o DynamoDB Local aceita quaisquer valores.
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");
    }

    public ValueTask InitializeAsync() => new(_dynamoDb.StartAsync());

    public override async ValueTask DisposeAsync()
    {
        await _dynamoDb.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // ConfigureAppConfiguration adiciona fontes APÓS os defaults (appsettings.json), então estes
        // valores têm prioridade.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamoDb:ServiceUrl"] = _dynamoDb.GetConnectionString(),
                ["DynamoDb:TabelaPagamentos"] = TabelaPagamentos,
                ["DynamoDb:CriarTabelaSeNaoExistir"] = "true",
                ["MercadoPago:BaseUrl"] = "http://localhost/mock/mercadopago",
                ["MercadoPago:ClientId"] = MercadoPagoClientId,
                ["MercadoPago:ClientSecret"] = MercadoPagoClientSecret,
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = AppIssuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:ValidIssuers:0"] = AppIssuer,
                ["Jwt:ValidIssuers:1"] = ClienteAuthIssuer,
                ["Mensageria:Transporte"] = "InMemory",
                ["OpenApi:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Test harness do MassTransit sobre a configuração do próprio host: expõe ITestHarness
            // (publicar eventos e aguardar consumo/publicação) mantendo os consumers reais do serviço.
            services.AddMassTransitTestHarness();

            // O cliente HTTP do processador chama o mock do próprio host de teste (não há porta real).
            services.AddHttpClient("IProcessadorPagamentoGateway")
                .ConfigurePrimaryHttpMessageHandler(sp => new GanchoDoProcessadorHandler(
                    ((TestServer)sp.GetRequiredService<IServer>()).CreateHandler(), this));
        });
    }

    /// <summary>
    /// Gancho de teste executado antes de cada requisição do adapter ao mock do Mercado Pago. Permite
    /// segurar chamadas simultâneas de pagamento até que todas tenham lido o pagamento Pendente.
    /// Nulo (sem efeito) fora dos testes que o usam; os testes da coleção rodam em série.
    /// </summary>
    public Func<HttpRequestMessage, Task>? AntesDeChamarOProcessador { get; set; }

    private sealed class GanchoDoProcessadorHandler(HttpMessageHandler inner, PagamentoWebApplicationFactory factory)
        : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (factory.AntesDeChamarOProcessador is { } gancho)
                await gancho(request);

            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Harness do MassTransit do host de teste (barramento em memória com os consumers reais).</summary>
    public ITestHarness Harness => Services.GetRequiredService<ITestHarness>();

    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Cliente com token de papel Oficina (emissor do serviço de autenticação).</summary>
    public HttpClient CreateOficinaClient() =>
        CreateAuthenticatedClient(GerarToken(AppIssuer, Guid.NewGuid(), "Oficina"));

    /// <summary>Cliente com token de papel Cliente (emissor da Function Serverless de autenticação por CPF).</summary>
    public HttpClient CreateClienteClient(Guid clienteId) =>
        CreateAuthenticatedClient(GerarToken(ClienteAuthIssuer, clienteId, "Cliente"));

    private static string GerarToken(string issuer, Guid sub, string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, sub.ToString()),
            new Claim("role", role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Define a coleção "Integration" — todos os testes com [Collection("Integration")] compartilham a
/// mesma instância de <see cref="PagamentoWebApplicationFactory"/>. O xunit.v3 garante que os testes
/// da coleção NÃO rodam em paralelo entre si.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<PagamentoWebApplicationFactory> { }
