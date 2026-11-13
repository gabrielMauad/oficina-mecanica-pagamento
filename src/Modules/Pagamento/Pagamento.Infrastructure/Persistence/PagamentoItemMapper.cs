using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Pagamento.Adapters.DataSources.Records;

namespace Pagamento.Infrastructure.Persistence;

/// <summary>
/// Converte o <see cref="PagamentoRecord"/> de/para os atributos do item da tabela de pagamentos.
/// Chave: <c>OrdemServicoId</c> (partição) + <c>OrcamentoId</c> (ordenação). Atributos opcionais
/// (<c>MotivoRecusa</c>, <c>MercadoPagoPaymentId</c>) só existem no item quando têm valor.
/// </summary>
internal static class PagamentoItemMapper
{
    public const string OrdemServicoId = "OrdemServicoId";
    public const string OrcamentoId = "OrcamentoId";
    public const string PagamentoId = "PagamentoId";
    public const string ClienteId = "ClienteId";
    public const string Valor = "Valor";
    public const string Status = "Status";
    public const string MotivoRecusa = "MotivoRecusa";
    public const string MercadoPagoPaymentId = "MercadoPagoPaymentId";
    public const string CriadoEm = "CriadoEm";
    public const string AtualizadoEm = "AtualizadoEm";

    public static Dictionary<string, AttributeValue> ToItem(PagamentoRecord record)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [OrdemServicoId] = new() { S = record.OrdemServicoId.ToString() },
            [OrcamentoId] = new() { S = record.OrcamentoId.ToString() },
            [PagamentoId] = new() { S = record.PagamentoId.ToString() },
            [ClienteId] = new() { S = record.ClienteId.ToString() },
            [Valor] = new() { N = record.Valor.ToString(CultureInfo.InvariantCulture) },
            [Status] = new() { S = record.Status },
            [CriadoEm] = new() { S = record.CriadoEm.ToString("O", CultureInfo.InvariantCulture) },
            [AtualizadoEm] = new() { S = record.AtualizadoEm.ToString("O", CultureInfo.InvariantCulture) }
        };

        if (record.MotivoRecusa is not null)
            item[MotivoRecusa] = new AttributeValue { S = record.MotivoRecusa };
        if (record.MercadoPagoPaymentId is { } paymentId)
            item[MercadoPagoPaymentId] = new AttributeValue { N = paymentId.ToString(CultureInfo.InvariantCulture) };

        return item;
    }

    public static PagamentoRecord ToRecord(IReadOnlyDictionary<string, AttributeValue> item) =>
        new(
            Guid.Parse(item[PagamentoId].S),
            Guid.Parse(item[OrdemServicoId].S),
            Guid.Parse(item[OrcamentoId].S),
            Guid.Parse(item[ClienteId].S),
            decimal.Parse(item[Valor].N, CultureInfo.InvariantCulture),
            item[Status].S,
            item.TryGetValue(MotivoRecusa, out var motivo) ? motivo.S : null,
            item.TryGetValue(MercadoPagoPaymentId, out var paymentId)
                ? long.Parse(paymentId.N, CultureInfo.InvariantCulture)
                : null,
            ParseData(item[CriadoEm].S),
            ParseData(item[AtualizadoEm].S));

    private static DateTime ParseData(string valor) =>
        DateTime.Parse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
