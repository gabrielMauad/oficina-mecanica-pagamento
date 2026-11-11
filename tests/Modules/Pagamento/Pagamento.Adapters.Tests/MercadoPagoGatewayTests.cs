using System.Net;
using System.Text.Json;
using Pagamento.Adapters.Gateways;
using Pagamento.Application.Gateways.Dtos;

namespace Pagamento.Adapters.Tests;

public class MercadoPagoGatewayTests
{
    private const string TokenJson = """{"access_token":"APP_USR-token-de-teste","token_type":"bearer","expires_in":21600}""";

    private static readonly MercadoPagoCredenciais Credenciais = new("mp-dev-client", "mp-dev-secret");

    private static SolicitacaoPagamento NovaSolicitacao(string titular = "APRO") =>
        new(350.75m, Guid.Parse("11111111-2222-3333-4444-555555555555"), "Pagamento da OS", titular);

    private static (MercadoPagoGateway Gateway, FakeHttpMessageHandler Handler) Criar(
        Func<RegisteredRequest, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/mock/mercadopago/") };
        return (new MercadoPagoGateway(httpClient, Credenciais), handler);
    }

    private static HttpResponseMessage RespostaPadrao(RegisteredRequest request, string pagamentoJson) =>
        request.Path.EndsWith("/oauth/token")
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenJson)
            : FakeHttpMessageHandler.Json(HttpStatusCode.Created, pagamentoJson);

    [Fact(DisplayName = "Fluxo — obtém o token por credenciais de cliente e cria o pagamento com Bearer")]
    public async Task Processar_DeveObterTokenECriarPagamentoComBearer()
    {
        // Arrange
        var (gateway, handler) = Criar(r => RespostaPadrao(r,
            """{"id":98765432101,"status":"approved","status_detail":"accredited"}"""));

        // Act
        var resultado = await gateway.Processar(NovaSolicitacao());

        // Assert — primeiro o token, depois o pagamento
        Assert.Equal(2, handler.Requests.Count);

        var token = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, token.Message.Method);
        Assert.Equal("/mock/mercadopago/oauth/token", token.Path);
        using var tokenBody = JsonDocument.Parse(token.Body!);
        Assert.Equal("mp-dev-client", tokenBody.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("mp-dev-secret", tokenBody.RootElement.GetProperty("client_secret").GetString());
        Assert.Equal("client_credentials", tokenBody.RootElement.GetProperty("grant_type").GetString());

        var pagamento = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, pagamento.Message.Method);
        Assert.Equal("/mock/mercadopago/v1/payments", pagamento.Path);
        Assert.Equal("Bearer", pagamento.Message.Headers.Authorization!.Scheme);
        Assert.Equal("APP_USR-token-de-teste", pagamento.Message.Headers.Authorization.Parameter);
        Assert.Equal(
            "11111111-2222-3333-4444-555555555555",
            Assert.Single(pagamento.Message.Headers.GetValues("X-Idempotency-Key")));
        using var pagamentoBody = JsonDocument.Parse(pagamento.Body!);
        Assert.Equal(350.75m, pagamentoBody.RootElement.GetProperty("transaction_amount").GetDecimal());
        Assert.Equal("11111111-2222-3333-4444-555555555555", pagamentoBody.RootElement.GetProperty("external_reference").GetString());
        Assert.Equal("Pagamento da OS", pagamentoBody.RootElement.GetProperty("description").GetString());
        Assert.Equal("APRO", pagamentoBody.RootElement.GetProperty("card_holder_name").GetString());

        Assert.True(resultado.Aprovado);
        Assert.Equal(98765432101, resultado.PagamentoExternoId);
        Assert.Equal("accredited", resultado.DetalheStatus);
    }

    [Fact(DisplayName = "Pagamento rejected — resultado recusado com o status_detail")]
    public async Task Processar_Rejected_DeveRetornarRecusado()
    {
        var (gateway, _) = Criar(r => RespostaPadrao(r,
            """{"id":12345678901,"status":"rejected","status_detail":"cc_rejected_other_reason"}"""));

        var resultado = await gateway.Processar(NovaSolicitacao("OTHE"));

        Assert.False(resultado.Aprovado);
        Assert.Equal(12345678901, resultado.PagamentoExternoId);
        Assert.Equal("cc_rejected_other_reason", resultado.DetalheStatus);
    }

    [Fact(DisplayName = "Status diferente de approved sem detalhe — recusado com o próprio status como motivo")]
    public async Task Processar_StatusDesconhecidoSemDetalhe_DeveUsarStatusComoMotivo()
    {
        var (gateway, _) = Criar(r => RespostaPadrao(r, """{"id":5,"status":"in_process"}"""));

        var resultado = await gateway.Processar(NovaSolicitacao());

        Assert.False(resultado.Aprovado);
        Assert.Equal("in_process", resultado.DetalheStatus);
    }

    [Fact(DisplayName = "Credenciais recusadas (401 no token) — propaga HttpRequestException e não cria pagamento")]
    public async Task Processar_TokenRecusado_DeveLancar()
    {
        var (gateway, handler) = Criar(_ => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() => gateway.Processar(NovaSolicitacao()));

        var unica = Assert.Single(handler.Requests);
        Assert.EndsWith("/oauth/token", unica.Path);
    }

    [Fact(DisplayName = "Falha ao criar o pagamento (500) — propaga HttpRequestException")]
    public async Task Processar_PagamentoComErroDoServidor_DeveLancar()
    {
        var (gateway, _) = Criar(r => r.Path.EndsWith("/oauth/token")
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenJson)
            : FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() => gateway.Processar(NovaSolicitacao()));
    }

    [Fact(DisplayName = "Resposta de token sem access_token — lança InvalidOperationException")]
    public async Task Processar_TokenSemAccessToken_DeveLancar()
    {
        var (gateway, _) = Criar(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"token_type":"bearer"}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.Processar(NovaSolicitacao()));
    }

    [Fact(DisplayName = "Resposta de pagamento sem corpo — lança InvalidOperationException")]
    public async Task Processar_PagamentoSemCorpo_DeveLancar()
    {
        var (gateway, _) = Criar(r => r.Path.EndsWith("/oauth/token")
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, TokenJson)
            : FakeHttpMessageHandler.Json(HttpStatusCode.Created, "null"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.Processar(NovaSolicitacao()));
    }
}
