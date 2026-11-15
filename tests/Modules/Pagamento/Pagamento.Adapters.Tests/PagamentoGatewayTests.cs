using Moq;
using Pagamento.Adapters.DataSources;
using Pagamento.Adapters.DataSources.Mappers;
using Pagamento.Adapters.DataSources.Records;
using Pagamento.Adapters.Gateways;
using Pagamento.Domain;
using Pagamento.Domain.Events;
using SharedKernel.Application;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Adapters.Tests;

public class PagamentoGatewayTests
{
    private readonly Mock<IPagamentoRepository> _repositoryMock = new();
    private readonly DomainEventCollector _collector = new();
    private readonly PagamentoGateway _gateway;

    private readonly Guid _ordemServicoId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();

    public PagamentoGatewayTests()
    {
        _gateway = new PagamentoGateway(_repositoryMock.Object, _collector);
    }

    private PagamentoEntity Novo(DateTime? criadoEm = null, StatusPagamento status = StatusPagamento.Pendente) =>
        PagamentoEntity.Reconstituir(
            PagamentoId.Novo(), _ordemServicoId, Guid.NewGuid(), _clienteId, 100m, status,
            status == StatusPagamento.Recusado ? "motivo" : null, null,
            criadoEm ?? DateTime.UtcNow, criadoEm ?? DateTime.UtcNow);

    [Fact(DisplayName = "Adicionar — persiste o record do pagamento")]
    public async Task Adicionar_DevePersistirRecord()
    {
        var pagamento = PagamentoEntity.Criar(_ordemServicoId, Guid.NewGuid(), _clienteId, 321.5m).Value;

        await _gateway.Adicionar(pagamento);

        _repositoryMock.Verify(r => r.Adicionar(
            It.Is<PagamentoRecord>(x =>
                x.PagamentoId == pagamento.Id.Value
                && x.OrdemServicoId == _ordemServicoId
                && x.Valor == 321.5m
                && x.Status == "Pendente"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "AtualizarSePendente — persiste o record e registra o agregado para publicar seus eventos de domínio")]
    public async Task AtualizarSePendente_DevePersistirERegistrarAgregado()
    {
        var pagamento = PagamentoEntity.Criar(_ordemServicoId, Guid.NewGuid(), _clienteId, 100m).Value;
        pagamento.Aprovar(55);
        _repositoryMock
            .Setup(r => r.AtualizarSePendente(It.IsAny<PagamentoRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var gravou = await _gateway.AtualizarSePendente(pagamento);

        Assert.True(gravou);
        _repositoryMock.Verify(r => r.AtualizarSePendente(
            It.Is<PagamentoRecord>(x => x.Status == "Aprovado" && x.MercadoPagoPaymentId == 55),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<PagamentoAprovado>(Assert.Single(_collector.Coletar()));
    }

    [Fact(DisplayName = "AtualizarSePendente — gravação condicional perdida devolve false e não registra os eventos de domínio")]
    public async Task AtualizarSePendente_CondicaoFalha_NaoDeveRegistrarAgregado()
    {
        var pagamento = PagamentoEntity.Criar(_ordemServicoId, Guid.NewGuid(), _clienteId, 100m).Value;
        pagamento.Aprovar(55);
        _repositoryMock
            .Setup(r => r.AtualizarSePendente(It.IsAny<PagamentoRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var gravou = await _gateway.AtualizarSePendente(pagamento);

        Assert.False(gravou);
        Assert.Empty(_collector.Coletar());
    }

    [Fact(DisplayName = "ObterPorOrdemServicoEOrcamento — devolve o agregado reconstituído")]
    public async Task ObterPorOrdemServicoEOrcamento_DeveReconstituir()
    {
        var orcamentoId = Guid.NewGuid();
        var pagamento = Novo();
        _repositoryMock
            .Setup(r => r.ObterPorOrdemServicoEOrcamento(_ordemServicoId, orcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagamentoMapper.ToRecord(pagamento));

        var obtido = await _gateway.ObterPorOrdemServicoEOrcamento(_ordemServicoId, orcamentoId);

        Assert.NotNull(obtido);
        Assert.Equal(pagamento.Id, obtido.Id);
    }

    [Fact(DisplayName = "ObterPorOrdemServicoEOrcamento — sem registro devolve null")]
    public async Task ObterPorOrdemServicoEOrcamento_SemRegistro_DeveDevolverNull()
    {
        _repositoryMock
            .Setup(r => r.ObterPorOrdemServicoEOrcamento(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoRecord?)null);

        Assert.Null(await _gateway.ObterPorOrdemServicoEOrcamento(_ordemServicoId, Guid.NewGuid()));
    }

    [Fact(DisplayName = "ObterPendentePorOrdemServico — ignora aprovados/recusados e devolve o pendente mais recente")]
    public async Task ObterPendentePorOrdemServico_DeveDevolverPendenteMaisRecente()
    {
        var agora = DateTime.UtcNow;
        var recusado = Novo(agora.AddHours(-3), StatusPagamento.Recusado);
        var pendenteAntigo = Novo(agora.AddHours(-2));
        var pendenteRecente = Novo(agora.AddHours(-1));
        _repositoryMock
            .Setup(r => r.ListarPorOrdemServico(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                PagamentoMapper.ToRecord(pendenteAntigo),
                PagamentoMapper.ToRecord(recusado),
                PagamentoMapper.ToRecord(pendenteRecente)]);

        var pendente = await _gateway.ObterPendentePorOrdemServico(_ordemServicoId);

        Assert.NotNull(pendente);
        Assert.Equal(pendenteRecente.Id, pendente.Id);
    }

    [Fact(DisplayName = "ObterPendentePorOrdemServico — sem pendente devolve null")]
    public async Task ObterPendentePorOrdemServico_SemPendente_DeveDevolverNull()
    {
        _repositoryMock
            .Setup(r => r.ListarPorOrdemServico(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([PagamentoMapper.ToRecord(Novo(status: StatusPagamento.Recusado))]);

        Assert.Null(await _gateway.ObterPendentePorOrdemServico(_ordemServicoId));
    }

    [Fact(DisplayName = "ListarPorOrdemServico — devolve os pagamentos ordenados pela criação")]
    public async Task ListarPorOrdemServico_DeveOrdenarPorCriacao()
    {
        var agora = DateTime.UtcNow;
        var primeiro = Novo(agora.AddHours(-2));
        var segundo = Novo(agora.AddHours(-1));
        _repositoryMock
            .Setup(r => r.ListarPorOrdemServico(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([PagamentoMapper.ToRecord(segundo), PagamentoMapper.ToRecord(primeiro)]);

        var lista = await _gateway.ListarPorOrdemServico(_ordemServicoId);

        Assert.Equal([primeiro.Id, segundo.Id], lista.Select(p => p.Id));
    }

    [Fact(DisplayName = "RegistrarCancelamento — delega ao repositório com o motivo e devolve o resultado da gravação condicional")]
    public async Task RegistrarCancelamento_DeveDelegarAoRepositorio()
    {
        var orcamentoId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.RegistrarCancelamento(_ordemServicoId, orcamentoId, "Estoque insuficiente", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await _gateway.RegistrarCancelamento(_ordemServicoId, orcamentoId, "Estoque insuficiente"));
    }

    [Fact(DisplayName = "ObterMotivoCancelamentoRegistrado — delega ao repositório")]
    public async Task ObterMotivoCancelamentoRegistrado_DeveDelegarAoRepositorio()
    {
        var orcamentoId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.ObterMotivoCancelamentoRegistrado(_ordemServicoId, orcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Estoque insuficiente");

        Assert.Equal("Estoque insuficiente", await _gateway.ObterMotivoCancelamentoRegistrado(_ordemServicoId, orcamentoId));
    }

    [Fact(DisplayName = "SubstituirCancelamentoRegistrado — persiste o record do pagamento cancelado")]
    public async Task SubstituirCancelamentoRegistrado_DevePersistir()
    {
        var pagamento = PagamentoEntity.Criar(_ordemServicoId, Guid.NewGuid(), _clienteId, 100m).Value;
        pagamento.Cancelar("Estoque insuficiente");
        _repositoryMock
            .Setup(r => r.SubstituirCancelamentoRegistrado(It.IsAny<PagamentoRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var gravou = await _gateway.SubstituirCancelamentoRegistrado(pagamento);

        Assert.True(gravou);
        _repositoryMock.Verify(r => r.SubstituirCancelamentoRegistrado(
            It.Is<PagamentoRecord>(x => x.PagamentoId == pagamento.Id.Value && x.Status == "Cancelado"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "SubstituirCancelamentoRegistrado — gravação condicional perdida devolve false")]
    public async Task SubstituirCancelamentoRegistrado_CondicaoFalha_DeveDevolverFalse()
    {
        var pagamento = PagamentoEntity.Criar(_ordemServicoId, Guid.NewGuid(), _clienteId, 100m).Value;
        pagamento.Cancelar("Estoque insuficiente");
        _repositoryMock
            .Setup(r => r.SubstituirCancelamentoRegistrado(It.IsAny<PagamentoRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Assert.False(await _gateway.SubstituirCancelamentoRegistrado(pagamento));
    }
}
