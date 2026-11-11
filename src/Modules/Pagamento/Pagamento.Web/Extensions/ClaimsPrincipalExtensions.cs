using System.Security.Claims;

namespace Pagamento.Web.Extensions;

internal static class ClaimsPrincipalExtensions
{
    // Nome curto da claim padrão do JWT (RFC 7519) — RoleClaimType/NameClaimType em
    // AuthenticationExtensions preservam os nomes originais "role"/"sub" (MapInboundClaims = false).
    private const string SubClaimType = "sub";

    /// <summary>
    /// Id do cliente autenticado (claim `sub`), quando o papel do token é `Cliente`.
    /// Retorna null para o papel `Oficina`, que não tem restrição de dono.
    /// </summary>
    public static Guid? ObterClienteIdDoSolicitante(this ClaimsPrincipal user)
    {
        if (!user.IsInRole("Cliente"))
            return null;

        Guid.TryParse(user.FindFirstValue(SubClaimType), out var clienteId);
        return clienteId;
    }
}
