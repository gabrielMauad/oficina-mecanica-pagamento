using Scalar.AspNetCore;

namespace Api.Extensions;

internal static class OpenApiExtensions
{
    /// <summary>
    /// Expõe o documento OpenAPI e a UI do Scalar, controlado pela flag de configuração
    /// "OpenApi:Enabled" — independente do ambiente (ASPNETCORE_ENVIRONMENT), para permitir
    /// documentação acessível em ambientes publicados sem recompilar a aplicação.
    /// </summary>
    public static WebApplication MapOpenApiDocumentation(this WebApplication app)
    {
        var enabled = app.Configuration.GetValue("OpenApi:Enabled", defaultValue: true);

        if (!enabled)
            return app;

        app.MapOpenApi();
        app.MapScalarApiReference(option =>
        {
            option.Title = "Oficina Mecanica API";
        });

        app.MapGet("/", () => Results.Redirect("/scalar"));

        return app;
    }
}
