using System.Security.Cryptography;
using System.Text;

namespace Api.MercadoPagoMock;

/// <summary>
/// Emite e valida o <c>access_token</c> do mock do Mercado Pago sem guardar estado: o token é
/// <c>APP_USR-{expiração}-{assinatura}</c>, em que a assinatura é um HMAC-SHA256 da expiração, calculado
/// com o <c>client_secret</c> (e o <c>client_id</c>) configurados. Qualquer réplica do serviço, com as
/// mesmas credenciais, valida um token emitido por outra.
/// </summary>
internal sealed class MercadoPagoMockTokenService(TimeProvider relogio)
{
    public const int ValidadeEmSegundos = 21600;

    private const string Prefixo = "APP_USR";

    public string Emitir(string clientId, string clientSecret)
    {
        var expiraEm = relogio.GetUtcNow().ToUnixTimeSeconds() + ValidadeEmSegundos;
        return $"{Prefixo}-{expiraEm}-{Assinar(clientId, clientSecret, expiraEm)}";
    }

    public bool Validar(string? token, string clientId, string clientSecret)
    {
        var partes = token?.Split('-');
        if (partes is not [Prefixo, var expiracao, var assinatura] || !long.TryParse(expiracao, out var expiraEm))
            return false;

        if (relogio.GetUtcNow().ToUnixTimeSeconds() >= expiraEm)
            return false;

        return CredenciaisIguais(Assinar(clientId, clientSecret, expiraEm), assinatura);
    }

    /// <summary>Comparação em tempo constante, para não vazar por tempo de resposta o quanto do valor confere.</summary>
    public static bool CredenciaisIguais(string? esperado, string? recebido)
    {
        if (esperado is null || recebido is null)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(esperado)),
            SHA256.HashData(Encoding.UTF8.GetBytes(recebido)));
    }

    private static string Assinar(string clientId, string clientSecret, long expiraEm)
    {
        var assinatura = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(clientSecret),
            Encoding.UTF8.GetBytes($"{clientId}.{expiraEm}"));
        return Convert.ToHexString(assinatura);
    }
}
