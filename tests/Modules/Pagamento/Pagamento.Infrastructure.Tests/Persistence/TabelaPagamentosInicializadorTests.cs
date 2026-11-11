using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Pagamento.Infrastructure.Persistence;

namespace Pagamento.Infrastructure.Tests.Persistence;

public class TabelaPagamentosInicializadorTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();

    private TabelaPagamentosInicializador Criar(bool criarTabela, string? tabela = "tabela-de-teste") =>
        new(_dynamoDb.Object, Options.Create(new DynamoDbOptions
        {
            TabelaPagamentos = tabela,
            CriarTabelaSeNaoExistir = criarTabela
        }));

    private void TabelaFicaAtiva() =>
        _dynamoDb
            .Setup(d => d.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = TableStatus.ACTIVE } });

    [Fact(DisplayName = "Flag desligada (AWS) — não toca no DynamoDB")]
    public async Task FlagDesligada_NaoDeveChamarODynamoDb()
    {
        await Criar(criarTabela: false).GarantirTabelaAsync();

        _dynamoDb.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Flag ligada — cria a tabela com a chave do contrato, sob demanda, e espera ficar ativa")]
    public async Task FlagLigada_DeveCriarTabelaComAChaveDoContrato()
    {
        // Arrange
        CreateTableRequest? requisicao = null;
        _dynamoDb
            .Setup(d => d.CreateTableAsync(It.IsAny<CreateTableRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTableRequest, CancellationToken>((r, _) => requisicao = r)
            .ReturnsAsync(new CreateTableResponse());
        TabelaFicaAtiva();

        // Act
        await Criar(criarTabela: true).GarantirTabelaAsync();

        // Assert
        Assert.NotNull(requisicao);
        Assert.Equal("tabela-de-teste", requisicao.TableName);
        Assert.Equal(BillingMode.PAY_PER_REQUEST, requisicao.BillingMode);
        Assert.Collection(requisicao.KeySchema,
            hash =>
            {
                Assert.Equal("OrdemServicoId", hash.AttributeName);
                Assert.Equal(KeyType.HASH, hash.KeyType);
            },
            range =>
            {
                Assert.Equal("OrcamentoId", range.AttributeName);
                Assert.Equal(KeyType.RANGE, range.KeyType);
            });
        Assert.All(requisicao.AttributeDefinitions, a => Assert.Equal(ScalarAttributeType.S, a.AttributeType));
        Assert.Equal(["OrdemServicoId", "OrcamentoId"], requisicao.AttributeDefinitions.Select(a => a.AttributeName));
        _dynamoDb.Verify(d => d.DescribeTableAsync("tabela-de-teste", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Tabela já existente — ignora o conflito e segue para a espera")]
    public async Task TabelaJaExiste_DeveIgnorarConflito()
    {
        // Arrange
        _dynamoDb
            .Setup(d => d.CreateTableAsync(It.IsAny<CreateTableRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceInUseException("já existe"));
        TabelaFicaAtiva();

        // Act
        await Criar(criarTabela: true).GarantirTabelaAsync();

        // Assert
        _dynamoDb.Verify(d => d.DescribeTableAsync("tabela-de-teste", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Tabela ainda CREATING — consulta de novo até ficar ativa")]
    public async Task TabelaCreating_DeveAguardarFicarAtiva()
    {
        // Arrange
        _dynamoDb
            .Setup(d => d.CreateTableAsync(It.IsAny<CreateTableRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTableResponse());
        _dynamoDb
            .SetupSequence(d => d.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = TableStatus.CREATING } })
            .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = TableStatus.ACTIVE } });

        // Act
        await Criar(criarTabela: true).GarantirTabelaAsync();

        // Assert
        _dynamoDb.Verify(d => d.DescribeTableAsync("tabela-de-teste", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact(DisplayName = "Flag ligada sem nome da tabela — lança InvalidOperationException")]
    public async Task SemNomeDaTabela_DeveLancar()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Criar(criarTabela: true, tabela: null).GarantirTabelaAsync());
    }
}
