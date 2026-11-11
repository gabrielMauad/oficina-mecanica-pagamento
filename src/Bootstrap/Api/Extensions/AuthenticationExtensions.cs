using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Api.Extensions;

/// <summary>
/// Configura autenticação JWT (HS256, segredo compartilhado — ver ADR-001) e autorização por
/// papel (ver ADR-003). A aplicação aceita tokens de dois emissores: o serviço de autenticação
/// (papel Oficina) e a Function Serverless de autenticação por CPF (papel Cliente). Todo token
/// precisa trazer um issuer listado em Jwt:ValidIssuers e a audience Jwt:Audience.
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddOficinaMecanicaAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwtSecret = configuration["Jwt:Secret"]
                    ?? throw new InvalidOperationException("JWT secret não configurado.");

                var audience = configuration["Jwt:Audience"]
                    ?? throw new InvalidOperationException("JWT audience não configurada.");

                var validIssuers = configuration.GetSection("Jwt:ValidIssuers").Get<string[]>();
                if (validIssuers is null || validIssuers.Length == 0)
                    throw new InvalidOperationException("JWT valid issuers não configurados.");

                // As claims são emitidas com os nomes curtos padrão (sub, role) tanto pela
                // aplicação quanto pela Function Serverless — desliga o remapeamento para
                // ClaimTypes.* e usa RoleClaimType/NameClaimType explícitos.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateIssuer = true,
                    ValidIssuers = validIssuers,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    RoleClaimType = "role",
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization();

        return services;
    }
}
