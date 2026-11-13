namespace Pagamento.Infrastructure.Persistence;

/// <summary>
/// Opções da seção <c>DynamoDb</c> (variáveis <c>DynamoDb__*</c>). Na AWS a tabela vem do Terraform,
/// <see cref="ServiceUrl"/> fica vazio e as credenciais vêm da cadeia padrão do SDK. Localmente,
/// <see cref="ServiceUrl"/> aponta para o LocalStack/DynamoDB Local e
/// <see cref="CriarTabelaSeNaoExistir"/> faz o serviço criar a tabela na partida.
/// </summary>
public sealed class DynamoDbOptions
{
    public const string SectionName = "DynamoDb";

    public string? TabelaPagamentos { get; set; }
    public string Regiao { get; set; } = "us-east-1";
    public string? ServiceUrl { get; set; }
    public bool CriarTabelaSeNaoExistir { get; set; }
}
