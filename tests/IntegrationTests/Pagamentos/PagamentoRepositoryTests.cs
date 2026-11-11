using Amazon.DynamoDBv2.Model;
using IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pagamento.Adapters.DataSources;
using Pagamento.Adapters.DataSources.Records;

namespace IntegrationTests.Pagamentos;

/// <summary>Repositório de pagamentos contra um DynamoDB real (DynamoDB Local).</summary>
[Collection("Integration")]
public class PagamentoRepositoryTests(PagamentoWebApplicationFactory factory)
{
    private static PagamentoRecord NovoRecord(Guid ordemServicoId, Guid orcamentoId, DateTime criadoEm) =>
        new(Guid.NewGuid(), ordemServicoId, orcamentoId, Guid.NewGuid(), 99.90m,
            "Pendente", null, null, criadoEm, criadoEm);

    private IServiceScope NovoEscopo()
    {
        _ = factory.Server; // garante que o host foi construído (e a tabela criada)
        return factory.Services.CreateScope();
    }

    [Fact(DisplayName = "Adicionar e obter — ida e volta pelo DynamoDB preserva o record")]
    public async Task AdicionarEObter_DevePreservarRecord()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        // Act
        await repositorio.Adicionar(record);
        var obtido = await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId);

        // Assert
        Assert.Equal(record, obtido);
    }

    [Fact(DisplayName = "Adicionar o mesmo par (ordem de serviço, orçamento) duas vezes falha e preserva o original")]
    public async Task Adicionar_Duplicado_DeveFalharSemSobrescrever()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var original = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await repositorio.Adicionar(original);

        // Act & Assert
        await Assert.ThrowsAsync<ConditionalCheckFailedException>(
            () => repositorio.Adicionar(original with { PagamentoId = Guid.NewGuid(), Valor = 1m }));
        var obtido = await repositorio.ObterPorOrdemServicoEOrcamento(original.OrdemServicoId, original.OrcamentoId);
        Assert.Equal(original, obtido);
    }

    [Fact(DisplayName = "Atualizar — grava o novo estado, com os atributos opcionais")]
    public async Task Atualizar_DeveGravarNovoEstado()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await repositorio.Adicionar(record);
        var recusado = record with
        {
            Status = "Recusado",
            MotivoRecusa = "cc_rejected_other_reason",
            MercadoPagoPaymentId = 98765432101,
            AtualizadoEm = record.CriadoEm.AddSeconds(5)
        };

        // Act
        await repositorio.Atualizar(recusado);

        // Assert
        Assert.Equal(recusado, await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
    }

    [Fact(DisplayName = "Obter — par inexistente devolve null")]
    public async Task Obter_Inexistente_DeveDevolverNull()
    {
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();

        Assert.Null(await repositorio.ObterPorOrdemServicoEOrcamento(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact(DisplayName = "Listar — devolve só os pagamentos da ordem de serviço informada")]
    public async Task Listar_DeveDevolverSoOsDaOrdemDeServico()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var ordemServicoId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        var primeiro = NovoRecord(ordemServicoId, Guid.NewGuid(), agora);
        var segundo = NovoRecord(ordemServicoId, Guid.NewGuid(), agora.AddSeconds(1));
        await repositorio.Adicionar(primeiro);
        await repositorio.Adicionar(segundo);
        await repositorio.Adicionar(NovoRecord(Guid.NewGuid(), Guid.NewGuid(), agora));

        // Act
        var lista = await repositorio.ListarPorOrdemServico(ordemServicoId);

        // Assert
        Assert.Equal(2, lista.Count);
        Assert.Contains(primeiro, lista);
        Assert.Contains(segundo, lista);
    }

    [Fact(DisplayName = "Listar — ordem de serviço sem pagamentos devolve lista vazia")]
    public async Task Listar_Vazio_DeveDevolverListaVazia()
    {
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();

        Assert.Empty(await repositorio.ListarPorOrdemServico(Guid.NewGuid()));
    }
}
