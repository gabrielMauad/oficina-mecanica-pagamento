using System.Text;
using System.Text.Json;
using Api.Logging;
using Api.Middlewares;
using Microsoft.AspNetCore.Http;
using IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntegrationTests.Host;

/// <summary>Componentes do host (exception handler, formatter de log) exercitados diretamente.</summary>
public class ApiComponentsTests
{
    private sealed class ProblemDetailsServiceFake : IProblemDetailsService
    {
        public ProblemDetailsContext? Contexto { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Contexto = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Contexto = context;
            return ValueTask.FromResult(true);
        }
    }

    [Theory(DisplayName = "CustomExceptionHandler — ArgumentException vira 400 e as demais viram 500")]
    [InlineData(typeof(ArgumentException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(ArgumentNullException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status500InternalServerError)]
    public async Task ExceptionHandler_DeveMapearStatus(Type tipoExcecao, int statusEsperado)
    {
        var service = new ProblemDetailsServiceFake();
        var handler = new CustomExceptionHandler(service);
        var httpContext = new DefaultHttpContext();
        var excecao = (Exception)Activator.CreateInstance(tipoExcecao, "falhou")!;

        var tratada = await handler.TryHandleAsync(httpContext, excecao, TestContext.Current.CancellationToken);

        Assert.True(tratada);
        Assert.Equal(statusEsperado, httpContext.Response.StatusCode);
        Assert.NotNull(service.Contexto);
        Assert.Same(excecao, service.Contexto!.Exception);
        Assert.Equal(tipoExcecao.Name, service.Contexto.ProblemDetails.Type);
        Assert.Equal("An error occurred", service.Contexto.ProblemDetails.Title);
        Assert.Contains("falhou", service.Contexto.ProblemDetails.Detail);
    }

    private static JsonElement Formatar(
        string mensagem, Exception? excecao = null, IExternalScopeProvider? escopos = null,
        IEnumerable<KeyValuePair<string, object>>? estado = null)
    {
        var formatter = new TraceJsonConsoleFormatter();
        var writer = new StringWriter(new StringBuilder());
        IEnumerable<KeyValuePair<string, object>> state = estado ?? [];

        formatter.Write(
            new LogEntry<IEnumerable<KeyValuePair<string, object>>>(
                LogLevel.Warning, "Categoria.Teste", new EventId(1), state, excecao, (_, _) => mensagem),
            escopos,
            writer);

        var linha = writer.ToString();
        Assert.EndsWith(Environment.NewLine, linha);
        return JsonDocument.Parse(linha).RootElement.Clone();
    }

    [Fact(DisplayName = "TraceJsonConsoleFormatter — emite uma linha JSON com nível, categoria e mensagem")]
    public void Formatter_DeveEmitirJson()
    {
        var json = Formatar("olá");

        Assert.Equal("Warning", json.GetProperty("level").GetString());
        Assert.Equal("Categoria.Teste", json.GetProperty("category").GetString());
        Assert.Equal("olá", json.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("trace_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("span_id").ValueKind);
    }

    [Fact(DisplayName = "TraceJsonConsoleFormatter — inclui trace_id e span_id da Activity corrente")]
    public void Formatter_DeveIncluirTraceId()
    {
        using var source = new System.Diagnostics.ActivitySource("teste-formatter");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("op");
        Assert.NotNull(activity);

        var json = Formatar("com trace");

        Assert.Equal(activity!.TraceId.ToHexString(), json.GetProperty("trace_id").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), json.GetProperty("span_id").GetString());
    }

    private sealed class EscopoFake(IEnumerable<KeyValuePair<string, object>> itens) : IExternalScopeProvider
    {
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) => callback(itens, state);

        public IDisposable Push(object? state) => throw new NotSupportedException();
    }

    [Fact(DisplayName = "TraceJsonConsoleFormatter — grava propriedades do estado e do escopo com o tipo JSON certo")]
    public void Formatter_DevePropagarPropriedadesDeEstadoEEscopo()
    {
        KeyValuePair<string, object>[] estado =
        [
            new("{OriginalFormat}", "ignorada"),
            new("texto", "abc"),
            new("flag", true),
            new("inteiro", 42),
            new("decimal", 1.5m),
            new("data", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
            new("dataOffset", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            new("guid", Guid.Empty),
            new("nulo", null!)
        ];
        var escopo = new EscopoFake([new KeyValuePair<string, object>("pedido", "p-1")]);

        var json = Formatar("msg", escopos: escopo, estado: estado);

        Assert.False(json.TryGetProperty("{OriginalFormat}", out _));
        Assert.Equal("abc", json.GetProperty("texto").GetString());
        Assert.True(json.GetProperty("flag").GetBoolean());
        Assert.Equal(42, json.GetProperty("inteiro").GetInt64());
        Assert.Equal(1.5, json.GetProperty("decimal").GetDouble());
        Assert.StartsWith("2026-01-02T03:04:05", json.GetProperty("data").GetString());
        Assert.StartsWith("2026-01-02T03:04:05", json.GetProperty("dataOffset").GetString());
        Assert.Equal(Guid.Empty.ToString(), json.GetProperty("guid").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nulo").ValueKind);
        Assert.Equal("p-1", json.GetProperty("pedido").GetString());
    }

    [Fact(DisplayName = "TraceJsonConsoleFormatter — inclui a exceção; sem mensagem nem exceção, não escreve nada")]
    public void Formatter_ExcecaoEMensagemVazia()
    {
        var json = Formatar("erro", new InvalidOperationException("quebrou"));
        var excecao = json.GetProperty("exception");
        Assert.Equal(typeof(InvalidOperationException).FullName, excecao.GetProperty("type").GetString());
        Assert.Equal("quebrou", excecao.GetProperty("message").GetString());

        var formatter = new TraceJsonConsoleFormatter();
        var writer = new StringWriter();
        formatter.Write(
            new LogEntry<string>(LogLevel.Information, "c", new EventId(0), "", null, (s, _) => s),
            null,
            writer);
        Assert.Equal(string.Empty, writer.ToString());
    }
}

/// <summary>Documentação OpenAPI exposta pelo host quando a flag OpenApi:Enabled está ligada.</summary>
[Collection("Integration")]
public class OpenApiDocumentationTests(PagamentoWebApplicationFactory factory)
{
    private WebApplicationFactory<Program> ComOpenApi(bool habilitado) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenApi:Enabled"] = habilitado.ToString()
                })));

    [Fact(DisplayName = "OpenAPI habilitado — documento declara o esquema Bearer e a raiz redireciona ao Scalar")]
    public async Task OpenApiHabilitado_DeveExporDocumentoComBearer()
    {
        await using var app = ComOpenApi(true);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });

        var resposta = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        resposta.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var bearer = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var raiz = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, raiz.StatusCode);
        Assert.Equal("/scalar", raiz.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "OpenAPI habilitado — operações autorizadas referenciam o esquema Bearer; a raiz não declara segurança")]
    public async Task OpenApiHabilitado_OperacoesAutorizadasReferenciamBearer()
    {
        await using var app = ComOpenApi(true);
        using var client = app.CreateClient();

        var json = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths");
        var operacoes = paths.EnumerateObject()
            .Where(p => p.Name.StartsWith("/api/v1/pagamentos"))
            .SelectMany(p => p.Value.EnumerateObject())
            .Select(op => op.Value)
            .ToList();
        Assert.NotEmpty(operacoes);
        Assert.All(operacoes, op =>
        {
            Assert.True(op.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
            // O requisito deve referenciar o esquema Bearer; um objeto vazio ({}) não protege nada.
            Assert.True(security[0].TryGetProperty("Bearer", out _));
        });
        Assert.False(paths.GetProperty("/").GetProperty("get").TryGetProperty("security", out _));
    }

    [Fact(DisplayName = "OpenAPI desabilitado — documento não é exposto")]
    public async Task OpenApiDesabilitado_NaoDeveExporDocumento()
    {
        await using var app = ComOpenApi(false);
        using var client = app.CreateClient();

        var resposta = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
