using System.Net.Http.Json;
using System.Text.Json;
using OficinaMecanica.Contracts.IntegrationEvents;

namespace IntegrationTests.Infrastructure;

/// <summary>Resposta de pagamento do contrato HTTP do serviço.</summary>
public sealed record PagamentoDto(
    Guid PagamentoId,
    Guid OrdemServicoId,
    Guid OrcamentoId,
    decimal Valor,
    string Status,
    string? MotivoRecusa,
    DateTime CriadoEm,
    DateTime AtualizadoEm);

public static class PagamentoTestHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Publica no barramento o evento que a OS emite quando o cliente aprova o orçamento.</summary>
    public static async Task PublicarOrcamentoAprovadoAsync(
        PagamentoWebApplicationFactory factory, Guid ordemServicoId, Guid orcamentoId, Guid clienteId, decimal valor)
    {
        await factory.Harness.Start();
        await factory.Harness.Bus.Publish(new OrcamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, ordemServicoId, orcamentoId, clienteId, valor));
    }

    /// <summary>Publica no barramento o evento que o PecasInsumos emite quando a baixa de estoque falha.</summary>
    public static async Task<Guid> PublicarEstoqueBaixaFalhouAsync(
        PagamentoWebApplicationFactory factory, Guid ordemServicoId, Guid orcamentoId, string motivo = "Estoque insuficiente")
    {
        await factory.Harness.Start();
        var eventId = Guid.NewGuid();
        await factory.Harness.Bus.Publish(new EstoqueBaixaFalhouIntegrationEvent(
            eventId, DateTime.UtcNow, ordemServicoId, orcamentoId, motivo));
        return eventId;
    }

    public static async Task<List<PagamentoDto>> ListarAsync(HttpClient client, Guid ordemServicoId)
    {
        var response = await client.GetAsync($"/api/v1/pagamentos/ordens-servico/{ordemServicoId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<PagamentoDto>>(JsonOptions))!;
    }

    /// <summary>
    /// O consumo é assíncrono (barramento): consulta a API até a lista ter o tamanho esperado ou até o
    /// limite de espera. Não usa a espera por inatividade do harness, que já pode ter expirado quando
    /// outros testes da coleção rodaram antes (o host de teste é compartilhado).
    /// </summary>
    public static async Task<List<PagamentoDto>> AguardarPagamentosAsync(
        HttpClient oficina, Guid ordemServicoId, int quantidadeEsperada)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        var pagamentos = await ListarAsync(oficina, ordemServicoId);
        while (pagamentos.Count < quantidadeEsperada && DateTime.UtcNow < limite)
        {
            await Task.Delay(50);
            pagamentos = await ListarAsync(oficina, ordemServicoId);
        }

        return pagamentos;
    }

    public static Task<HttpResponseMessage> PagarAsync(HttpClient client, Guid ordemServicoId, string titular) =>
        client.PostAsJsonAsync($"/api/v1/pagamentos/ordens-servico/{ordemServicoId}/pagar", new { titularCartao = titular });

    /// <summary>Cria um pagamento Pendente via evento e devolve (ordem de serviço, orçamento, cliente).</summary>
    public static async Task<(Guid OrdemServicoId, Guid OrcamentoId, Guid ClienteId)> CriarPendenteAsync(
        PagamentoWebApplicationFactory factory, decimal valor = 275.40m)
    {
        var ordemServicoId = Guid.NewGuid();
        var orcamentoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();

        await PublicarOrcamentoAprovadoAsync(factory, ordemServicoId, orcamentoId, clienteId, valor);
        using var oficina = factory.CreateOficinaClient();
        var pagamentos = await AguardarPagamentosAsync(oficina, ordemServicoId, 1);
        Assert.Single(pagamentos);

        return (ordemServicoId, orcamentoId, clienteId);
    }
}
