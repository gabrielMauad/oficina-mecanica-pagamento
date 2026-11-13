using System.Net;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace IntegrationTests.HealthChecks;

[Collection("Integration")]
public class HealthCheckEndpointsTests(PagamentoWebApplicationFactory factory)
{
    [Theory(DisplayName = "Health checks anônimos respondem 200")]
    [InlineData("/healthz")]
    [InlineData("/healthz/live")]
    [InlineData("/healthz/ready")]
    public async Task HealthChecks_DevemResponder200SemToken(string rota)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(rota);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "Readiness — inclui o check do DynamoDB e o do barramento de mensageria")]
    public async Task Ready_DeveIncluirDynamoDbEBarramento()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz/ready");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checks = json.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["dynamodb"]);
        Assert.Contains("masstransit-bus", checks.Keys);
    }

    [Fact(DisplayName = "Liveness — não depende de nenhuma dependência externa")]
    public async Task Live_NaoDeveTerChecks()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz/live");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("checks").GetArrayLength());
    }

    [Fact(DisplayName = "Tabela inexistente — readiness fica 503 enquanto liveness segue 200")]
    public async Task TabelaInexistente_ReadyDeveFalharELiveContinuar()
    {
        // Arrange — host cuja tabela não existe e que não a cria
        await using var semTabela = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DynamoDb:TabelaPagamentos"] = "tabela-que-nao-existe",
                    ["DynamoDb:CriarTabelaSeNaoExistir"] = "false"
                })));
        using var client = semTabela.CreateClient();

        // Act
        var ready = await client.GetAsync("/healthz/ready");
        var live = await client.GetAsync("/healthz/live");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
