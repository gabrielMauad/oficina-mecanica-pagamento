using Microsoft.Extensions.Options;
using Pagamento.Infrastructure.Http;

namespace Api.MercadoPagoMock;

/// <summary>Categoria de log do mock do Mercado Pago.</summary>
internal sealed class MercadoPagoMockLog;

/// <summary>
/// Mock do Mercado Pago exposto pelo próprio host, simulando um sistema externo: nada aqui pertence
/// ao domínio do serviço de Pagamento, que o consome por HTTP pela <c>MercadoPago__BaseUrl</c>, como
/// consumiria a API real. Imita o fluxo do Mercado Pago: <c>POST oauth/token</c> troca as
/// credenciais de cliente por um token e <c>POST v1/payments</c> cria o pagamento com esse token
/// como Bearer. Convenção dos cartões de teste do Mercado Pago: titular <c>APRO</c> aprova; qualquer
/// outro valor recusa.
/// </summary>
internal static class MercadoPagoMockEndpoints
{
    public const string PathBase = "/mock/mercadopago";

    private const string TitularAprovado = "APRO";
    private const string Tag = "Mock Mercado Pago";

    public static IServiceCollection AddMercadoPagoMock(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MercadoPagoMockTokenService>();
        return services;
    }

    public static IEndpointRouteBuilder MapMercadoPagoMock(this IEndpointRouteBuilder endpoints)
    {
        // Rotas anônimas para o JWT da oficina: o mock tem a própria autenticação (token do mock).
        var mock = endpoints.MapGroup(PathBase).AllowAnonymous().WithTags(Tag);

        mock.MapPost("/oauth/token", EmitirToken)
            .WithSummary("Troca as credenciais de cliente por um access_token do mock.");
        mock.MapPost("/v1/payments", CriarPagamento)
            .WithSummary("Cria um pagamento: titular APRO aprova, qualquer outro valor recusa.");

        return endpoints;
    }

    private static IResult EmitirToken(
        MockTokenRequest requisicao,
        IOptions<MercadoPagoOptions> options,
        MercadoPagoMockTokenService tokens,
        ILogger<MercadoPagoMockLog> logger)
    {
        var credenciais = options.Value;

        if (requisicao.GrantType != "client_credentials")
            return Results.Problem(
                title: "grant_type não suportado: use client_credentials.",
                statusCode: StatusCodes.Status400BadRequest);

        if (!MercadoPagoMockTokenService.CredenciaisIguais(credenciais.ClientId, requisicao.ClientId)
            || !MercadoPagoMockTokenService.CredenciaisIguais(credenciais.ClientSecret, requisicao.ClientSecret))
        {
            logger.LogWarning("Mock Mercado Pago: credenciais de cliente recusadas.");
            return Results.Problem(
                title: "Credenciais de cliente inválidas.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var token = tokens.Emitir(credenciais.ClientId!, credenciais.ClientSecret!);
        return Results.Ok(new MockTokenResponse(token, "bearer", MercadoPagoMockTokenService.ValidadeEmSegundos));
    }

    private static IResult CriarPagamento(
        HttpRequest request,
        MockPaymentRequest pagamento,
        IOptions<MercadoPagoOptions> options,
        MercadoPagoMockTokenService tokens,
        ILogger<MercadoPagoMockLog> logger)
    {
        var credenciais = options.Value;

        if (!tokens.Validar(ExtrairBearer(request), credenciais.ClientId!, credenciais.ClientSecret!))
            return Results.Problem(
                title: "access_token inválido ou expirado.",
                statusCode: StatusCodes.Status401Unauthorized);

        if (pagamento.TransactionAmount <= 0)
            return Results.Problem(
                title: "transaction_amount deve ser maior que zero.",
                statusCode: StatusCodes.Status400BadRequest);

        var aprovado = pagamento.CardHolderName == TitularAprovado;
        var resposta = new MockPaymentResponse(
            Random.Shared.NextInt64(10_000_000_000, 100_000_000_000),
            aprovado ? "approved" : "rejected",
            aprovado ? "accredited" : "cc_rejected_other_reason");

        logger.LogInformation(
            "Mock Mercado Pago: pagamento {PagamentoExternoId} do orçamento {ExternalReference} {Status} ({StatusDetail}).",
            resposta.Id, pagamento.ExternalReference, resposta.Status, resposta.StatusDetail);

        return Results.Json(resposta, statusCode: StatusCodes.Status201Created);
    }

    private static string? ExtrairBearer(HttpRequest request)
    {
        const string prefixo = "Bearer ";
        var cabecalho = request.Headers.Authorization.ToString();
        return cabecalho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
            ? cabecalho[prefixo.Length..].Trim()
            : null;
    }
}
