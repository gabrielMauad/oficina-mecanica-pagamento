using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Pagamento.Adapters.DataSources;
using Pagamento.Adapters.DataSources.Records;

namespace Pagamento.Infrastructure.Persistence;

/// <summary>
/// <see cref="IPagamentoRepository"/> sobre a tabela de pagamentos do DynamoDB. As leituras são
/// consistentes: um pagamento recém-criado pelo consumidor precisa estar visível para quem paga em seguida.
/// </summary>
public sealed class PagamentoRepository : IPagamentoRepository
{
    private const string StatusPendente = "Pendente";

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tabela;

    public PagamentoRepository(IAmazonDynamoDB dynamoDb, IOptions<DynamoDbOptions> options)
    {
        _dynamoDb = dynamoDb;
        _tabela = options.Value.TabelaPagamentos
            ?? throw new InvalidOperationException("DynamoDb:TabelaPagamentos não configurado.");
    }

    public async Task Adicionar(PagamentoRecord record, CancellationToken ct = default)
    {
        // A condição na chave (ordem de serviço + orçamento) impede que duas entregas concorrentes do
        // mesmo evento criem o pagamento duas vezes; a perdedora falha e é reprocessada pelo retry.
        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tabela,
            Item = PagamentoItemMapper.ToItem(record),
            ConditionExpression = $"attribute_not_exists({PagamentoItemMapper.OrdemServicoId})"
        }, ct);
    }

    public async Task<bool> AtualizarSePendente(PagamentoRecord record, CancellationToken ct = default)
    {
        // A condição no status persistido impede que duas chamadas concorrentes (ex.: dois pagamentos
        // simultâneos da mesma ordem de serviço, ou um pagamento e um cancelamento) resolvam o mesmo
        // pagamento: só a primeira grava; a outra recebe false e não deve publicar nada.
        try
        {
            await _dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = _tabela,
                Item = PagamentoItemMapper.ToItem(record),
                ConditionExpression = "#status = :pendente",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#status"] = PagamentoItemMapper.Status
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pendente"] = new() { S = StatusPendente }
                }
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<PagamentoRecord?> ObterPorOrdemServicoEOrcamento(
        Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default)
    {
        var response = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _tabela,
            Key = new Dictionary<string, AttributeValue>
            {
                [PagamentoItemMapper.OrdemServicoId] = new() { S = ordemServicoId.ToString() },
                [PagamentoItemMapper.OrcamentoId] = new() { S = orcamentoId.ToString() }
            },
            ConsistentRead = true
        }, ct);

        return response.Item is { Count: > 0 } ? PagamentoItemMapper.ToRecord(response.Item) : null;
    }

    public async Task<IReadOnlyList<PagamentoRecord>> ListarPorOrdemServico(
        Guid ordemServicoId, CancellationToken ct = default)
    {
        var records = new List<PagamentoRecord>();
        Dictionary<string, AttributeValue>? paginaSeguinte = null;

        do
        {
            var response = await _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tabela,
                KeyConditionExpression = $"{PagamentoItemMapper.OrdemServicoId} = :ordemServicoId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":ordemServicoId"] = new() { S = ordemServicoId.ToString() }
                },
                ConsistentRead = true,
                ExclusiveStartKey = paginaSeguinte
            }, ct);

            records.AddRange(response.Items.Select(PagamentoItemMapper.ToRecord));
            paginaSeguinte = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (paginaSeguinte is not null);

        return records;
    }
}
