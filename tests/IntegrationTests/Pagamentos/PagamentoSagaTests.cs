using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Infrastructure;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Infrastructure.Messaging;
using SharedKernel.Application;
using static IntegrationTests.Infrastructure.PagamentoTestHelpers;

namespace IntegrationTests.Pagamentos;

/// <summary>
/// Etapa de pagamento da Saga coreografada, de ponta a ponta: o evento OrcamentoAprovado cria o
/// pagamento Pendente; o cliente paga pela rota HTTP (o adapter fala com o mock do Mercado Pago por
/// HTTP, no mesmo host); o resultado sai como PagamentoAprovado ou PagamentoRecusado.
/// </summary>
[Collection("Integration")]
public class PagamentoSagaTests(PagamentoWebApplicationFactory factory)
{
    [Fact(DisplayName = "IIntegrationEventBus do container é o adapter MassTransit (não o bus in-memory)")]
    public void IntegrationEventBus_DeveSerOAdapterMassTransit()
    {
        _ = factory.Server; // garante que o host foi construído

        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>();

        Assert.IsType<MassTransitIntegrationEventBus>(bus);
    }

    [Fact(DisplayName = "OrcamentoAprovado consumido → pagamento Pendente com o valor e o cliente do evento")]
    public async Task OrcamentoAprovado_DeveCriarPagamentoPendente()
    {
        // Arrange
        var ordemServicoId = Guid.NewGuid();
        var orcamentoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        using var oficina = factory.CreateOficinaClient();

        // Act
        await PublicarOrcamentoAprovadoAsync(factory, ordemServicoId, orcamentoId, clienteId, 1234.56m);
        var pagamentos = await AguardarPagamentosAsync(oficina, ordemServicoId, 1);

        // Assert
        var pagamento = Assert.Single(pagamentos);
        Assert.NotEqual(Guid.Empty, pagamento.PagamentoId);
        Assert.Equal(ordemServicoId, pagamento.OrdemServicoId);
        Assert.Equal(orcamentoId, pagamento.OrcamentoId);
        Assert.Equal(1234.56m, pagamento.Valor);
        Assert.Equal("Pendente", pagamento.Status);
        Assert.Null(pagamento.MotivoRecusa);
        Assert.InRange(pagamento.CriadoEm, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact(DisplayName = "OrcamentoAprovado reentregue → o pagamento continua único")]
    public async Task OrcamentoAprovado_Reentregue_NaoDeveDuplicarPagamento()
    {
        // Arrange
        var ordemServicoId = Guid.NewGuid();
        var orcamentoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        using var oficina = factory.CreateOficinaClient();

        // Act
        await PublicarOrcamentoAprovadoAsync(factory, ordemServicoId, orcamentoId, clienteId, 100m);
        var primeiro = await AguardarPagamentosAsync(oficina, ordemServicoId, 1);
        await PublicarOrcamentoAprovadoAsync(factory, ordemServicoId, orcamentoId, clienteId, 100m);
        await AguardarConsumoDasDuasEntregasAsync(ordemServicoId);
        var depois = await ListarAsync(oficina, ordemServicoId);

        // Assert
        Assert.Single(depois);
        Assert.Equal(primeiro[0].PagamentoId, depois[0].PagamentoId);
    }

    /// <summary>Espera por polling (e não pela inatividade do harness) o consumo do EstoqueBaixaFalhou.</summary>
    private async Task AguardarConsumoAsync(Guid eventId)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < limite)
        {
            if (factory.Harness.Consumed
                .Select<EstoqueBaixaFalhouIntegrationEvent>(m => m.Context.Message.EventId == eventId)
                .Any())
                return;

            await Task.Delay(50);
        }

        Assert.Fail("O evento EstoqueBaixaFalhou não foi consumido a tempo.");
    }

    private async Task AguardarConsumoDasDuasEntregasAsync(Guid ordemServicoId)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < limite)
        {
            var consumidas = factory.Harness.Consumed
                .Select<OrcamentoAprovadoIntegrationEvent>(m => m.Context.Message.OrdemServicoId == ordemServicoId)
                .Count();
            if (consumidas >= 2)
                return;

            await Task.Delay(50);
        }

        Assert.Fail("A segunda entrega do evento não foi consumida a tempo.");
    }

    [Fact(DisplayName = "Pagar com titular APRO → 200 Aprovado e PagamentoAprovado publicado")]
    public async Task Pagar_Apro_DeveAprovarEPublicarPagamentoAprovado()
    {
        // Arrange
        var (ordemServicoId, orcamentoId, clienteId) = await CriarPendenteAsync(factory);
        using var cliente = factory.CreateClienteClient(clienteId);

        // Act
        var response = await PagarAsync(cliente, ordemServicoId, "APRO");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagamento = await response.Content.ReadFromJsonAsync<PagamentoDto>(JsonOptions);
        Assert.NotNull(pagamento);
        Assert.Equal("Aprovado", pagamento.Status);
        Assert.Null(pagamento.MotivoRecusa);
        Assert.Equal(ordemServicoId, pagamento.OrdemServicoId);
        Assert.Equal(orcamentoId, pagamento.OrcamentoId);

        Assert.True(await factory.Harness.Published.Any<PagamentoAprovadoIntegrationEvent>(m =>
            m.Context.Message.PagamentoId == pagamento.PagamentoId
            && m.Context.Message.OrdemServicoId == ordemServicoId
            && m.Context.Message.OrcamentoId == orcamentoId));
        Assert.False(await factory.Harness.Published.Any<PagamentoRecusadoIntegrationEvent>(m =>
            m.Context.Message.OrdemServicoId == ordemServicoId));

        var persistido = Assert.Single(await ListarAsync(cliente, ordemServicoId));
        Assert.Equal("Aprovado", persistido.Status);
    }

    [Fact(DisplayName = "Pagar com titular OTHE → 200 Recusado com o motivo e PagamentoRecusado publicado")]
    public async Task Pagar_Othe_DeveRecusarEPublicarPagamentoRecusado()
    {
        // Arrange
        var (ordemServicoId, orcamentoId, clienteId) = await CriarPendenteAsync(factory);
        using var cliente = factory.CreateClienteClient(clienteId);

        // Act
        var response = await PagarAsync(cliente, ordemServicoId, "OTHE");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagamento = await response.Content.ReadFromJsonAsync<PagamentoDto>(JsonOptions);
        Assert.NotNull(pagamento);
        Assert.Equal("Recusado", pagamento.Status);
        Assert.Equal("cc_rejected_other_reason", pagamento.MotivoRecusa);

        Assert.True(await factory.Harness.Published.Any<PagamentoRecusadoIntegrationEvent>(m =>
            m.Context.Message.PagamentoId == pagamento.PagamentoId
            && m.Context.Message.OrdemServicoId == ordemServicoId
            && m.Context.Message.OrcamentoId == orcamentoId
            && m.Context.Message.Motivo == "cc_rejected_other_reason"));
        Assert.False(await factory.Harness.Published.Any<PagamentoAprovadoIntegrationEvent>(m =>
            m.Context.Message.OrdemServicoId == ordemServicoId));

        var persistido = Assert.Single(await ListarAsync(cliente, ordemServicoId));
        Assert.Equal("Recusado", persistido.Status);
        Assert.Equal("cc_rejected_other_reason", persistido.MotivoRecusa);
    }

    [Fact(DisplayName = "Dois pagamentos simultâneos da mesma ordem → só um vence (200), o outro recebe 409 e só um evento é publicado")]
    public async Task Pagar_Simultaneo_DeveResolverUmaVezEPublicarUmEvento()
    {
        // Arrange — o gancho segura as duas cobranças até ambas terem lido o pagamento Pendente
        var (ordemServicoId, orcamentoId, clienteId) = await CriarPendenteAsync(factory);
        using var cliente = factory.CreateClienteClient(clienteId);
        var chegadas = 0;
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.AntesDeChamarOProcessador = async requisicao =>
        {
            if (requisicao.RequestUri is null || !requisicao.RequestUri.AbsolutePath.EndsWith("/v1/payments"))
                return;
            if (Interlocked.Increment(ref chegadas) == 2)
                liberar.SetResult();
            await liberar.Task.WaitAsync(TimeSpan.FromSeconds(15));
        };

        HttpResponseMessage[] respostas;
        try
        {
            // Act
            respostas = await Task.WhenAll(
                PagarAsync(cliente, ordemServicoId, "APRO"),
                PagarAsync(cliente, ordemServicoId, "OTHE"));
        }
        finally
        {
            factory.AntesDeChamarOProcessador = null;
        }

        // Assert
        var vencedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        var perdedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("Pagamento.ResolvidoPorOutraChamada", await perdedora.Content.ReadAsStringAsync());

        var resolvido = await vencedora.Content.ReadFromJsonAsync<PagamentoDto>(JsonOptions);
        Assert.NotNull(resolvido);
        var persistido = Assert.Single(await ListarAsync(cliente, ordemServicoId));
        Assert.Equal(resolvido.Status, persistido.Status);

        var aprovado = resolvido.Status == "Aprovado";
        Assert.True(aprovado
            ? await factory.Harness.Published.Any<PagamentoAprovadoIntegrationEvent>(m => m.Context.Message.OrdemServicoId == ordemServicoId)
            : await factory.Harness.Published.Any<PagamentoRecusadoIntegrationEvent>(m => m.Context.Message.OrdemServicoId == ordemServicoId));
        Assert.False(aprovado
            ? await factory.Harness.Published.Any<PagamentoRecusadoIntegrationEvent>(m => m.Context.Message.OrdemServicoId == ordemServicoId)
            : await factory.Harness.Published.Any<PagamentoAprovadoIntegrationEvent>(m => m.Context.Message.OrdemServicoId == ordemServicoId));
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou com pagamento Pendente → Cancelado, nada publicado e pagar passa a dar 404")]
    public async Task EstoqueBaixaFalhou_ComPendente_DeveCancelarSemPublicar()
    {
        // Arrange
        var (ordemServicoId, orcamentoId, clienteId) = await CriarPendenteAsync(factory);
        using var cliente = factory.CreateClienteClient(clienteId);

        // Act
        var eventId = await PublicarEstoqueBaixaFalhouAsync(factory, ordemServicoId, orcamentoId, "Estoque insuficiente");
        var cancelados = await AguardarStatusAsync(cliente, ordemServicoId, "Cancelado");

        // Assert
        var pagamento = Assert.Single(cancelados);
        Assert.Equal("Cancelado", pagamento.Status);
        Assert.Equal("Estoque insuficiente", pagamento.MotivoRecusa);
        await AguardarConsumoAsync(eventId);
        Assert.False(await factory.Harness.Published.Any<PagamentoAprovadoIntegrationEvent>(m =>
            m.Context.Message.OrdemServicoId == ordemServicoId));
        Assert.False(await factory.Harness.Published.Any<PagamentoRecusadoIntegrationEvent>(m =>
            m.Context.Message.OrdemServicoId == ordemServicoId));
        Assert.Equal(HttpStatusCode.NotFound, (await PagarAsync(cliente, ordemServicoId, "APRO")).StatusCode);
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou reentregue sobre pagamento já Cancelado → continua Cancelado")]
    public async Task EstoqueBaixaFalhou_Reentregue_DeveSerIdempotente()
    {
        // Arrange
        var (ordemServicoId, orcamentoId, clienteId) = await CriarPendenteAsync(factory);
        using var cliente = factory.CreateClienteClient(clienteId);
        await PublicarEstoqueBaixaFalhouAsync(factory, ordemServicoId, orcamentoId, "Estoque insuficiente");
        await AguardarStatusAsync(cliente, ordemServicoId, "Cancelado");

        // Act
        var segundoId = await PublicarEstoqueBaixaFalhouAsync(factory, ordemServicoId, orcamentoId, "Outro motivo");
        await AguardarConsumoAsync(segundoId);

        // Assert
        var pagamento = Assert.Single(await ListarAsync(cliente, ordemServicoId));
        Assert.Equal("Cancelado", pagamento.Status);
        Assert.Equal("Estoque insuficiente", pagamento.MotivoRecusa);
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou sem pagamento da ordem → nada acontece")]
    public async Task EstoqueBaixaFalhou_SemPagamento_NaoDeveFazerNada()
    {
        // Arrange
        var ordemServicoId = Guid.NewGuid();
        using var oficina = factory.CreateOficinaClient();

        // Act
        var eventId = await PublicarEstoqueBaixaFalhouAsync(factory, ordemServicoId, Guid.NewGuid());
        await AguardarConsumoAsync(eventId);

        // Assert
        Assert.Empty(await ListarAsync(oficina, ordemServicoId));
        Assert.False(await factory.Harness.Published.Any<PagamentoAprovadoIntegrationEvent>(m =>
            m.Context.Message.OrdemServicoId == ordemServicoId));
        Assert.False(await factory.Harness.Published.Any<PagamentoRecusadoIntegrationEvent>(m =>
            m.Context.Message.OrdemServicoId == ordemServicoId));
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou de outro orçamento da mesma ordem ou com pagamento já Aprovado → o pagamento não muda")]
    public async Task EstoqueBaixaFalhou_NaoPendenteOuOutroOrcamento_NaoDeveAlterar()
    {
        // Arrange — um pagamento já aprovado e um Pendente de outro orçamento da mesma ordem
        var (ordemServicoId, orcamentoAprovadoId, clienteId) = await CriarPendenteAsync(factory, 100m);
        using var cliente = factory.CreateClienteClient(clienteId);
        Assert.Equal(HttpStatusCode.OK, (await PagarAsync(cliente, ordemServicoId, "APRO")).StatusCode);
        var outroOrcamentoId = Guid.NewGuid();
        await PublicarOrcamentoAprovadoAsync(factory, ordemServicoId, outroOrcamentoId, clienteId, 150m);
        await AguardarPagamentosAsync(cliente, ordemServicoId, 2);

        // Act — a falha é do orçamento já aprovado/pago, não do pendente
        var eventId = await PublicarEstoqueBaixaFalhouAsync(factory, ordemServicoId, orcamentoAprovadoId);
        await AguardarConsumoAsync(eventId);

        // Assert
        var pagamentos = await ListarAsync(cliente, ordemServicoId);
        Assert.Contains(pagamentos, p => p.OrcamentoId == orcamentoAprovadoId && p.Status == "Aprovado");
        Assert.Contains(pagamentos, p => p.OrcamentoId == outroOrcamentoId && p.Status == "Pendente");
    }

    private static async Task<List<PagamentoDto>> AguardarStatusAsync(HttpClient cliente, Guid ordemServicoId, string status)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        var pagamentos = await ListarAsync(cliente, ordemServicoId);
        while (!pagamentos.Any(p => p.Status == status) && DateTime.UtcNow < limite)
        {
            await Task.Delay(50);
            pagamentos = await ListarAsync(cliente, ordemServicoId);
        }

        return pagamentos;
    }

    [Fact(DisplayName = "Pagar com cliente de outra ordem → 403, o pagamento segue Pendente e nada é publicado")]
    public async Task Pagar_OutroCliente_DeveRetornar403()
    {
        // Arrange
        var (ordemServicoId, _, _) = await CriarPendenteAsync(factory);
        using var outroCliente = factory.CreateClienteClient(Guid.NewGuid());

        // Act
        var response = await PagarAsync(outroCliente, ordemServicoId, "APRO");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var oficina = factory.CreateOficinaClient();
        Assert.Equal("Pendente", Assert.Single(await ListarAsync(oficina, ordemServicoId)).Status);
        Assert.False(await factory.Harness.Published.Any<PagamentoAprovadoIntegrationEvent>(m =>
            m.Context.Message.OrdemServicoId == ordemServicoId));
    }

    [Fact(DisplayName = "Pagar sem pagamento pendente → 404")]
    public async Task Pagar_SemPendente_DeveRetornar404()
    {
        using var cliente = factory.CreateClienteClient(Guid.NewGuid());

        var response = await PagarAsync(cliente, Guid.NewGuid(), "APRO");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "Pagar de novo depois de resolvido → 404 (não há mais pendente)")]
    public async Task Pagar_JaResolvido_DeveRetornar404()
    {
        // Arrange
        var (ordemServicoId, _, clienteId) = await CriarPendenteAsync(factory);
        using var cliente = factory.CreateClienteClient(clienteId);
        Assert.Equal(HttpStatusCode.OK, (await PagarAsync(cliente, ordemServicoId, "APRO")).StatusCode);

        // Act
        var segunda = await PagarAsync(cliente, ordemServicoId, "APRO");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, segunda.StatusCode);
    }

    [Fact(DisplayName = "Pagar com papel Oficina → 403 (só o cliente paga) e sem token → 401")]
    public async Task Pagar_SemPermissao_DeveNegar()
    {
        var ordemServicoId = Guid.NewGuid();
        using var oficina = factory.CreateOficinaClient();
        using var anonimo = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await PagarAsync(oficina, ordemServicoId, "APRO")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PagarAsync(anonimo, ordemServicoId, "APRO")).StatusCode);
    }

    [Fact(DisplayName = "Pagar com titular vazio → 422 e o pagamento segue Pendente")]
    public async Task Pagar_TitularVazio_DeveRetornar422()
    {
        // Arrange
        var (ordemServicoId, _, clienteId) = await CriarPendenteAsync(factory);
        using var cliente = factory.CreateClienteClient(clienteId);

        // Act
        var response = await PagarAsync(cliente, ordemServicoId, "");

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Pendente", Assert.Single(await ListarAsync(cliente, ordemServicoId)).Status);
    }

    [Fact(DisplayName = "Pagamento recusado → novo orçamento aprovado gera novo Pendente e pode ser pago")]
    public async Task AposRecusa_NovoOrcamentoAprovado_DevePermitirNovoPagamento()
    {
        // Arrange — primeiro orçamento recusado
        var (ordemServicoId, _, clienteId) = await CriarPendenteAsync(factory, 100m);
        using var cliente = factory.CreateClienteClient(clienteId);
        await PagarAsync(cliente, ordemServicoId, "OTHE");

        // Act — a OS volta a diagnóstico e o cliente aprova um novo orçamento
        var novoOrcamentoId = Guid.NewGuid();
        await PublicarOrcamentoAprovadoAsync(factory, ordemServicoId, novoOrcamentoId, clienteId, 180m);
        var pagamentos = await AguardarPagamentosAsync(cliente, ordemServicoId, 2);
        var resposta = await PagarAsync(cliente, ordemServicoId, "APRO");

        // Assert
        Assert.Equal(2, pagamentos.Count);
        Assert.Contains(pagamentos, p => p.Status == "Recusado" && p.Valor == 100m);
        Assert.Contains(pagamentos, p => p.Status == "Pendente" && p.OrcamentoId == novoOrcamentoId && p.Valor == 180m);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var pago = await resposta.Content.ReadFromJsonAsync<PagamentoDto>(JsonOptions);
        Assert.Equal(novoOrcamentoId, pago!.OrcamentoId);
        Assert.Equal("Aprovado", pago.Status);
    }

    [Fact(DisplayName = "Listar — dono e oficina veem os pagamentos; outro cliente recebe 403; sem token, 401")]
    public async Task Listar_DeveRespeitarPapeisEDono()
    {
        // Arrange
        var (ordemServicoId, _, clienteId) = await CriarPendenteAsync(factory);
        var rota = $"/api/v1/pagamentos/ordens-servico/{ordemServicoId}";
        using var dono = factory.CreateClienteClient(clienteId);
        using var oficina = factory.CreateOficinaClient();
        using var outro = factory.CreateClienteClient(Guid.NewGuid());
        using var anonimo = factory.CreateClient();

        // Act & Assert
        Assert.Equal(HttpStatusCode.OK, (await dono.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await oficina.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outro.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(rota)).StatusCode);
    }

    [Fact(DisplayName = "Listar — ordem de serviço sem pagamentos devolve lista vazia")]
    public async Task Listar_SemPagamentos_DeveDevolverListaVazia()
    {
        using var cliente = factory.CreateClienteClient(Guid.NewGuid());

        var pagamentos = await ListarAsync(cliente, Guid.NewGuid());

        Assert.Empty(pagamentos);
    }
}
