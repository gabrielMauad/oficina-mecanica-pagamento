namespace Pagamento.Infrastructure.Http;

/// <summary>
/// Opções da seção <c>MercadoPago</c> (variáveis <c>MercadoPago__*</c>), todas obrigatórias e validadas
/// na partida. <see cref="BaseUrl"/> é a URL base da API do Mercado Pago; aqui ela aponta para o mock
/// que o próprio serviço expõe. <see cref="ClientId"/> e <see cref="ClientSecret"/> são as credenciais de
/// cliente usadas para obter o token, e o mock as valida contra os mesmos valores.
/// </summary>
public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    public string? BaseUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}
