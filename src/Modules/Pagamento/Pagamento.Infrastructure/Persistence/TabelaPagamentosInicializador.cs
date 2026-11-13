using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace Pagamento.Infrastructure.Persistence;

/// <summary>
/// Cria a tabela de pagamentos quando <c>DynamoDb__CriarTabelaSeNaoExistir</c> está ligado
/// (ambientes local e de teste). Na AWS a tabela é provisionada pelo Terraform e este passo não roda.
/// A chave é a do contrato do serviço: <c>OrdemServicoId</c> (partição) e <c>OrcamentoId</c>
/// (ordenação), ambas texto, com cobrança sob demanda.
/// </summary>
public sealed class TabelaPagamentosInicializador
{
    private static readonly TimeSpan IntervaloEntreConsultas = TimeSpan.FromMilliseconds(250);
    private const int MaximoDeConsultas = 120;

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly DynamoDbOptions _options;

    public TabelaPagamentosInicializador(IAmazonDynamoDB dynamoDb, IOptions<DynamoDbOptions> options)
    {
        _dynamoDb = dynamoDb;
        _options = options.Value;
    }

    public async Task GarantirTabelaAsync(CancellationToken ct = default)
    {
        if (!_options.CriarTabelaSeNaoExistir)
            return;

        var tabela = _options.TabelaPagamentos
            ?? throw new InvalidOperationException("DynamoDb:TabelaPagamentos não configurado.");

        try
        {
            await _dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = tabela,
                BillingMode = BillingMode.PAY_PER_REQUEST,
                AttributeDefinitions =
                [
                    new AttributeDefinition(PagamentoItemMapper.OrdemServicoId, ScalarAttributeType.S),
                    new AttributeDefinition(PagamentoItemMapper.OrcamentoId, ScalarAttributeType.S)
                ],
                KeySchema =
                [
                    new KeySchemaElement(PagamentoItemMapper.OrdemServicoId, KeyType.HASH),
                    new KeySchemaElement(PagamentoItemMapper.OrcamentoId, KeyType.RANGE)
                ]
            }, ct);
        }
        catch (ResourceInUseException)
        {
            // Tabela já existe (outra réplica ou partida anterior): segue para a espera.
        }

        await AguardarTabelaAtivaAsync(tabela, ct);
    }

    private async Task AguardarTabelaAtivaAsync(string tabela, CancellationToken ct)
    {
        for (var tentativa = 0; tentativa < MaximoDeConsultas; tentativa++)
        {
            var descricao = await _dynamoDb.DescribeTableAsync(tabela, ct);
            if (descricao.Table.TableStatus == TableStatus.ACTIVE)
                return;

            await Task.Delay(IntervaloEntreConsultas, ct);
        }

        throw new InvalidOperationException($"A tabela DynamoDB '{tabela}' não ficou ativa a tempo.");
    }
}
