using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Pagamento.Infrastructure.Persistence;
using System.Text.Json;

namespace Api.Extensions;

/// <summary>
/// Configura health checks separando liveness (processo vivo, sem dependências) de
/// readiness (apto a atender tráfego). A readiness agrega os checks com a tag "ready": o do
/// DynamoDB, registrado aqui, e o "masstransit-bus", que o MassTransit registra
/// automaticamente com a mesma tag (estado do barramento de mensageria).
/// </summary>
public static class HealthCheckExtensions
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DynamoDbHealthCheck>(
                "dynamodb",
                timeout: TimeSpan.FromSeconds(3),
                tags: [ReadyTag]);

        return services;
    }

    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: só confirma que o processo está de pé, sem checar dependências externas.
        endpoints.MapHealthChecks("/healthz/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync
        }).AllowAnonymous();

        // Readiness: apto a atender tráfego, inclui checks marcados com a tag "ready" (DynamoDB e barramento MassTransit).
        endpoints.MapHealthChecks("/healthz/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponseAsync
        }).AllowAnonymous();

        // Mantido por compatibilidade com o smoke test da pipeline: agrega todos os checks.
        endpoints.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            ResponseWriter = WriteResponseAsync
        }).AllowAnonymous();

        return endpoints;
    }

    private static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}

/// <summary>
/// Readiness do DynamoDB: um <c>DescribeTable</c> da tabela de pagamentos confirma conectividade,
/// credenciais e que a tabela existe.
/// </summary>
internal sealed class DynamoDbHealthCheck(IAmazonDynamoDB dynamoDb, IOptions<DynamoDbOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await dynamoDb.DescribeTableAsync(options.Value.TabelaPagamentos, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Tabela de pagamentos do DynamoDB indisponível.", ex);
        }
    }
}
