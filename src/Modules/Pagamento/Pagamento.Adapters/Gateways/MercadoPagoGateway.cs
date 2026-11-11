using System.Net.Http.Headers;
using System.Net.Http.Json;
using Pagamento.Adapters.Gateways.Http;
using Pagamento.Application.Gateways;
using Pagamento.Application.Gateways.Dtos;

namespace Pagamento.Adapters.Gateways;

/// <summary>
/// Processador de pagamento sobre a API do Mercado Pago, no fluxo da API real: primeiro obtém o
/// token por credenciais de cliente (<c>POST oauth/token</c>) e depois cria o pagamento com esse
/// token como Bearer (<c>POST v1/payments</c>). A URL base vem do <see cref="HttpClient"/>.
/// </summary>
public sealed class MercadoPagoGateway : IProcessadorPagamentoGateway
{
    private const string StatusAprovado = "approved";

    private readonly HttpClient _httpClient;
    private readonly MercadoPagoCredenciais _credenciais;

    public MercadoPagoGateway(HttpClient httpClient, MercadoPagoCredenciais credenciais)
    {
        _httpClient = httpClient;
        _credenciais = credenciais;
    }

    public async Task<ResultadoProcessamentoPagamento> Processar(SolicitacaoPagamento solicitacao, CancellationToken ct = default)
    {
        var accessToken = await ObterAccessToken(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/payments")
        {
            Content = JsonContent.Create(new PagamentoRequisicao(
                solicitacao.Valor,
                solicitacao.OrcamentoId.ToString(),
                solicitacao.Descricao,
                solicitacao.TitularCartao))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // O Mercado Pago exige uma chave de idempotência por cobrança: o orçamento identifica a cobrança.
        request.Headers.Add("X-Idempotency-Key", solicitacao.OrcamentoId.ToString());

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var pagamento = await response.Content.ReadFromJsonAsync<PagamentoResposta>(ct)
            ?? throw new InvalidOperationException("O Mercado Pago respondeu ao pagamento sem corpo.");

        // Qualquer status diferente de "approved" é tratado como recusa, com o status_detail como motivo.
        var aprovado = pagamento.Status == StatusAprovado;
        return new ResultadoProcessamentoPagamento(
            pagamento.Id,
            aprovado,
            pagamento.StatusDetail ?? pagamento.Status);
    }

    private async Task<string> ObterAccessToken(CancellationToken ct)
    {
        var requisicao = new TokenRequisicao(_credenciais.ClientId, _credenciais.ClientSecret, "client_credentials");

        using var response = await _httpClient.PostAsJsonAsync("oauth/token", requisicao, ct);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResposta>(ct);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
            throw new InvalidOperationException("O Mercado Pago respondeu ao token sem access_token.");

        return token.AccessToken;
    }
}
