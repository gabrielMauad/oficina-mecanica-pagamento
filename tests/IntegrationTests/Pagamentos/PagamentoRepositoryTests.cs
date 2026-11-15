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

    [Fact(DisplayName = "AtualizarSePendente — grava o novo estado, com os atributos opcionais")]
    public async Task AtualizarSePendente_DeveGravarNovoEstado()
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
        var gravou = await repositorio.AtualizarSePendente(recusado);

        // Assert
        Assert.True(gravou);
        Assert.Equal(recusado, await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
    }

    [Theory(DisplayName = "AtualizarSePendente — item que não está mais Pendente não é sobrescrito e devolve false")]
    [InlineData("Aprovado")]
    [InlineData("Recusado")]
    [InlineData("Cancelado")]
    public async Task AtualizarSePendente_JaResolvido_NaoDeveSobrescrever(string statusPersistido)
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        var persistido = record with { Status = statusPersistido, MotivoRecusa = "motivo original" };
        await repositorio.Adicionar(persistido);

        // Act
        var gravou = await repositorio.AtualizarSePendente(
            record with { Status = "Aprovado", MercadoPagoPaymentId = 1, MotivoRecusa = null });

        // Assert
        Assert.False(gravou);
        Assert.Equal(persistido, await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
    }

    [Fact(DisplayName = "AtualizarSePendente — duas gravações concorrentes sobre o mesmo Pendente: só uma vence")]
    public async Task AtualizarSePendente_Concorrente_SoUmaVence()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await repositorio.Adicionar(record);
        var aprovado = record with { Status = "Aprovado", MercadoPagoPaymentId = 1 };
        var recusado = record with { Status = "Recusado", MotivoRecusa = "cc_rejected_other_reason", MercadoPagoPaymentId = 2 };

        // Act
        var resultados = await Task.WhenAll(
            repositorio.AtualizarSePendente(aprovado),
            repositorio.AtualizarSePendente(recusado));

        // Assert
        Assert.Single(resultados, r => r);
        var final = await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId);
        Assert.Contains(final, new[] { aprovado, recusado });
    }

    [Fact(DisplayName = "AtualizarSePendente — item inexistente devolve false e não cria o item")]
    public async Task AtualizarSePendente_Inexistente_NaoDeveCriar()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        // Act
        var gravou = await repositorio.AtualizarSePendente(record with { Status = "Aprovado" });

        // Assert
        Assert.False(gravou);
        Assert.Null(await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
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

    [Fact(DisplayName = "RegistrarCancelamento — chave livre grava o marcador, que não é um pagamento (não é obtido nem listado)")]
    public async Task RegistrarCancelamento_ChaveLivre_DeveGravarMarcadorQueNaoEPagamento()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var ordemServicoId = Guid.NewGuid();
        var orcamentoId = Guid.NewGuid();

        // Act
        var gravou = await repositorio.RegistrarCancelamento(ordemServicoId, orcamentoId, "Estoque insuficiente", DateTime.UtcNow);

        // Assert
        Assert.True(gravou);
        Assert.Equal("Estoque insuficiente", await repositorio.ObterMotivoCancelamentoRegistrado(ordemServicoId, orcamentoId));
        Assert.Null(await repositorio.ObterPorOrdemServicoEOrcamento(ordemServicoId, orcamentoId));
        Assert.Empty(await repositorio.ListarPorOrdemServico(ordemServicoId));
    }

    [Fact(DisplayName = "RegistrarCancelamento — chave já ocupada por um pagamento devolve false e não o sobrescreve")]
    public async Task RegistrarCancelamento_ComPagamento_NaoDeveSobrescrever()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await repositorio.Adicionar(record);

        // Act
        var gravou = await repositorio.RegistrarCancelamento(
            record.OrdemServicoId, record.OrcamentoId, "Estoque insuficiente", DateTime.UtcNow);

        // Assert
        Assert.False(gravou);
        Assert.Equal(record, await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
        Assert.Null(await repositorio.ObterMotivoCancelamentoRegistrado(record.OrdemServicoId, record.OrcamentoId));
    }

    [Fact(DisplayName = "RegistrarCancelamento — repetido devolve false e preserva o primeiro motivo")]
    public async Task RegistrarCancelamento_Repetido_DevePreservarOPrimeiro()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var ordemServicoId = Guid.NewGuid();
        var orcamentoId = Guid.NewGuid();
        await repositorio.RegistrarCancelamento(ordemServicoId, orcamentoId, "primeiro", DateTime.UtcNow);

        // Act
        var gravou = await repositorio.RegistrarCancelamento(ordemServicoId, orcamentoId, "segundo", DateTime.UtcNow);

        // Assert
        Assert.False(gravou);
        Assert.Equal("primeiro", await repositorio.ObterMotivoCancelamentoRegistrado(ordemServicoId, orcamentoId));
    }

    [Fact(DisplayName = "Adicionar — chave ocupada por marcador de cancelamento falha e não cria pagamento Pendente")]
    public async Task Adicionar_ComMarcador_DeveFalhar()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await repositorio.RegistrarCancelamento(record.OrdemServicoId, record.OrcamentoId, "Estoque insuficiente", DateTime.UtcNow);

        // Act & Assert
        await Assert.ThrowsAsync<ConditionalCheckFailedException>(() => repositorio.Adicionar(record));
        Assert.Null(await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
        Assert.Equal("Estoque insuficiente", await repositorio.ObterMotivoCancelamentoRegistrado(record.OrdemServicoId, record.OrcamentoId));
    }

    [Fact(DisplayName = "AtualizarSePendente — marcador de cancelamento não é Pendente: devolve false")]
    public async Task AtualizarSePendente_ComMarcador_DeveDevolverFalse()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await repositorio.RegistrarCancelamento(record.OrdemServicoId, record.OrcamentoId, "Estoque insuficiente", DateTime.UtcNow);

        // Act
        var gravou = await repositorio.AtualizarSePendente(record with { Status = "Aprovado" });

        // Assert
        Assert.False(gravou);
        Assert.Null(await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
    }

    [Fact(DisplayName = "SubstituirCancelamentoRegistrado — troca o marcador pelo pagamento Cancelado; repetido devolve false")]
    public async Task SubstituirCancelamentoRegistrado_DeveTrocarOMarcador()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow) with
        {
            Status = "Cancelado",
            MotivoRecusa = "Estoque insuficiente"
        };
        await repositorio.RegistrarCancelamento(record.OrdemServicoId, record.OrcamentoId, "Estoque insuficiente", DateTime.UtcNow);

        // Act
        var primeira = await repositorio.SubstituirCancelamentoRegistrado(record);
        var segunda = await repositorio.SubstituirCancelamentoRegistrado(record with { PagamentoId = Guid.NewGuid() });

        // Assert
        Assert.True(primeira);
        Assert.False(segunda);
        Assert.Equal(record, await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
        Assert.Null(await repositorio.ObterMotivoCancelamentoRegistrado(record.OrdemServicoId, record.OrcamentoId));
        Assert.Equal(record, Assert.Single(await repositorio.ListarPorOrdemServico(record.OrdemServicoId)));
    }

    [Fact(DisplayName = "SubstituirCancelamentoRegistrado — sem marcador devolve false e não cria o item")]
    public async Task SubstituirCancelamentoRegistrado_SemMarcador_NaoDeveCriar()
    {
        // Arrange
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow) with { Status = "Cancelado" };

        // Act
        var gravou = await repositorio.SubstituirCancelamentoRegistrado(record);

        // Assert
        Assert.False(gravou);
        Assert.Null(await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId));
    }

    [Fact(DisplayName = "Corrida — Adicionar e RegistrarCancelamento concorrentes na mesma chave: exatamente um ocupa a chave, nunca os dois")]
    public async Task AdicionarERegistrarCancelamento_Concorrentes_ExatamenteUmVence()
    {
        using var escopo = NovoEscopo();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IPagamentoRepository>();

        for (var rodada = 0; rodada < 20; rodada++)
        {
            // Arrange
            var record = NovoRecord(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
            using var largada = new ManualResetEventSlim(false);

            // Act
            var adicionar = Task.Run(async () =>
            {
                largada.Wait();
                try
                {
                    await repositorio.Adicionar(record);
                    return true;
                }
                catch (ConditionalCheckFailedException)
                {
                    return false;
                }
            });
            var registrar = Task.Run(() =>
            {
                largada.Wait();
                return repositorio.RegistrarCancelamento(
                    record.OrdemServicoId, record.OrcamentoId, "Estoque insuficiente", DateTime.UtcNow);
            });
            largada.Set();
            var adicionou = await adicionar;
            var registrou = await registrar;

            // Assert
            Assert.NotEqual(adicionou, registrou);
            var pagamento = await repositorio.ObterPorOrdemServicoEOrcamento(record.OrdemServicoId, record.OrcamentoId);
            var motivo = await repositorio.ObterMotivoCancelamentoRegistrado(record.OrdemServicoId, record.OrcamentoId);
            Assert.Equal(adicionou, pagamento is not null);
            Assert.Equal(registrou, motivo is not null);
        }
    }
}
