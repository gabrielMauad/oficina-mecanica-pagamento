using System.Net;
using System.Text;

namespace Pagamento.Adapters.Tests;

/// <summary>
/// <see cref="HttpMessageHandler"/> falso: registra cada requisição (com o corpo já lido, porque o
/// conteúdo é descartado depois do envio) e responde com o que o teste definir.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<RegisteredRequest, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<RegisteredRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var registered = new RegisteredRequest(request, body);
        Requests.Add(registered);
        return responder(registered);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed record RegisteredRequest(HttpRequestMessage Message, string? Body)
{
    public string Path => Message.RequestUri!.AbsolutePath;
}
