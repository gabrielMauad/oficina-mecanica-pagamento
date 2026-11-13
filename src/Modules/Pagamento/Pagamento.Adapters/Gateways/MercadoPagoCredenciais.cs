namespace Pagamento.Adapters.Gateways;

/// <summary>Credenciais de cliente (OAuth client credentials) usadas para obter o token do Mercado Pago.</summary>
public sealed record MercadoPagoCredenciais(string ClientId, string ClientSecret);
