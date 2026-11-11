namespace Pagamento.Infrastructure.Messaging;

/// <summary>
/// Opções da seção <c>Mensageria</c> (variáveis <c>Mensageria__*</c>). Na AWS, <see cref="ServiceUrl"/>,
/// <see cref="AccessKey"/> e <see cref="SecretKey"/> ficam vazios e as credenciais vêm da cadeia padrão do SDK.
/// </summary>
public sealed class MensageriaOptions
{
    public const string SectionName = "Mensageria";
    public const string TransporteAmazonSqs = "AmazonSqs";
    public const string TransporteInMemory = "InMemory";

    public string Transporte { get; set; } = TransporteAmazonSqs;
    public string Regiao { get; set; } = "us-east-1";
    public string? ServiceUrl { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}
