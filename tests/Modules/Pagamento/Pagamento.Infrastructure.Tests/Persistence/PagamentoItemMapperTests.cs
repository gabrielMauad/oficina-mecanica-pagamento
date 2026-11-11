using Amazon.DynamoDBv2.Model;
using Pagamento.Adapters.DataSources.Records;
using Pagamento.Infrastructure.Persistence;

namespace Pagamento.Infrastructure.Tests.Persistence;

public class PagamentoItemMapperTests
{
    private static PagamentoRecord NovoRecord(string status = "Pendente", string? motivo = null, long? paymentId = null) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1234.56m,
            status, motivo, paymentId,
            new DateTime(2026, 11, 10, 8, 30, 15, 123, DateTimeKind.Utc),
            new DateTime(2026, 11, 10, 8, 31, 0, DateTimeKind.Utc));

    [Fact(DisplayName = "Item — a chave é OrdemServicoId (S) + OrcamentoId (S) e os demais atributos seguem o contrato")]
    public void ToItem_DeveSeguirOContratoDaTabela()
    {
        // Arrange
        var record = NovoRecord("Recusado", "cc_rejected_other_reason", 98765432101);

        // Act
        var item = PagamentoItemMapper.ToItem(record);

        // Assert
        Assert.Equal(record.OrdemServicoId.ToString(), item["OrdemServicoId"].S);
        Assert.Equal(record.OrcamentoId.ToString(), item["OrcamentoId"].S);
        Assert.Equal(record.PagamentoId.ToString(), item["PagamentoId"].S);
        Assert.Equal(record.ClienteId.ToString(), item["ClienteId"].S);
        Assert.Equal("1234.56", item["Valor"].N);
        Assert.Equal("Recusado", item["Status"].S);
        Assert.Equal("cc_rejected_other_reason", item["MotivoRecusa"].S);
        Assert.Equal("98765432101", item["MercadoPagoPaymentId"].N);
        Assert.False(string.IsNullOrEmpty(item["CriadoEm"].S));
        Assert.False(string.IsNullOrEmpty(item["AtualizadoEm"].S));
    }

    [Fact(DisplayName = "Item — atributos opcionais ausentes não são gravados")]
    public void ToItem_SemOpcionais_NaoDeveGravarAtributos()
    {
        var item = PagamentoItemMapper.ToItem(NovoRecord());

        Assert.False(item.ContainsKey("MotivoRecusa"));
        Assert.False(item.ContainsKey("MercadoPagoPaymentId"));
    }

    [Theory(DisplayName = "Item — ida e volta preserva o record")]
    [InlineData("Pendente", null, null)]
    [InlineData("Aprovado", null, 4242L)]
    [InlineData("Recusado", "motivo", 7L)]
    public void ToRecord_IdaEVolta_DevePreservarRecord(string status, string? motivo, long? paymentId)
    {
        // Arrange
        var original = NovoRecord(status, motivo, paymentId);

        // Act
        var restaurado = PagamentoItemMapper.ToRecord(PagamentoItemMapper.ToItem(original));

        // Assert
        Assert.Equal(original, restaurado);
        Assert.Equal(DateTimeKind.Utc, restaurado.CriadoEm.Kind);
    }

    [Fact(DisplayName = "Item — decimal é gravado com ponto, independente da cultura")]
    public void ToItem_DecimalComPonto()
    {
        var anterior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");

            var item = PagamentoItemMapper.ToItem(NovoRecord());

            Assert.Equal("1234.56", item["Valor"].N);
            Assert.Equal(1234.56m, PagamentoItemMapper.ToRecord(item).Valor);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = anterior;
        }
    }

    [Fact(DisplayName = "Item — leitura aceita item vindo do DynamoDB (dicionário somente leitura)")]
    public void ToRecord_AceitaDicionarioSomenteLeitura()
    {
        var record = NovoRecord();
        IReadOnlyDictionary<string, AttributeValue> item = PagamentoItemMapper.ToItem(record);

        Assert.Equal(record, PagamentoItemMapper.ToRecord(item));
    }
}
