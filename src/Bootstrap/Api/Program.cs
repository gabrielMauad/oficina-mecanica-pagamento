using Api.Extensions;
using Api.MercadoPagoMock;
using Api.Middlewares;
using Api.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Pagamento.Infrastructure;
using Pagamento.Infrastructure.Persistence;
using Pagamento.Web;
using SharedKernel.Application;

var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredJsonLogging();
builder.AddOtlpLogging();
builder.AddOficinaMecanicaObservability();

builder.Services.AddOficinaMecanicaAuthentication(builder.Configuration);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var hasAuthorize = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        var hasAllowAnonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();

        if (hasAuthorize && !hasAllowAnonymous)
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference("Bearer"),
                        new List<string>()
                    }
                }
            ];
        }

        return Task.CompletedTask;
    });
});


builder.Services.AddApiHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CustomExceptionHandler>();
builder.Services.AddSharedKernelServices();
builder.Services.AddPagamentoModule(builder.Configuration);
builder.Services.AddMercadoPagoMock();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(PagamentoAssemblyMarker).Assembly);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// O mock do Mercado Pago usa o próprio token (não é um JWT da oficina): fica fora da autenticação
// JWT para que a validação do bearer do mock não gere falhas de JWT nos logs.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments(MercadoPagoMockEndpoints.PathBase),
    branch => branch.UseAuthentication());
app.UseAuthorization();

app.MapControllers();
app.MapMercadoPagoMock();

app.MapOpenApiDocumentation();
app.MapApiHealthChecks();

// Local/teste: cria a tabela de pagamentos (DynamoDb__CriarTabelaSeNaoExistir). Na AWS ela vem do Terraform.
await app.Services.GetRequiredService<TabelaPagamentosInicializador>().GarantirTabelaAsync();

await app.RunAsync();

// Necessário para o WebApplicationFactory<Program> nos testes de integração
public partial class Program { }
