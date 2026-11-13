using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Adapters.Controllers;
using Pagamento.Adapters.DataSources;
using Pagamento.Adapters.DataSources.Records;
using Pagamento.Adapters.Gateways;
using Pagamento.Adapters.Models.Requests;
using Pagamento.Application.Commands.CancelarPagamento;
using Pagamento.Application.Commands.CriarPagamento;
using Pagamento.Application.Gateways;
using Pagamento.Application.Gateways.Dtos;
using SharedKernel.Application;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Adapters.Tests;

/// <summary>
/// O módulo não tem <see cref="IUnitOfWork"/> (o DynamoDB grava na hora): verifica, com o pipeline
/// real do MediatR, que o <c>TransactionBehavior</c> funciona sem nenhuma unidade de trabalho e publica
/// os eventos de integração só depois que o handler persistiu.
/// </summary>
public class PagarOrdemServicoPipelineTests
{
    private readonly List<string> _ordem = [];
    private readonly Mock<IIntegrationEventBus> _bus = new();
    private readonly Mock<IProcessadorPagamentoGateway> _processador = new();
    private readonly ServiceProvider _provider;

    private readonly Guid _ordemServicoId = Guid.NewGuid();
    private readonly Guid _orcamentoId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();

    public PagarOrdemServicoPipelineTests()
    {
        _bus
            .Setup(b => b.Publish(It.IsAny<PagamentoAprovadoIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => _ordem.Add("publicou-aprovado"))
            .Returns(Task.CompletedTask);
        _bus
            .Setup(b => b.Publish(It.IsAny<PagamentoRecusadoIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => _ordem.Add("publicou-recusado"))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelServices();
        services.AddSingleton(_ordem);
        services.AddScoped<IPagamentoRepository, RepositorioEmMemoria>();
        services.AddScoped<IPagamentoGateway, PagamentoGateway>();
        services.AddScoped(_ => _processador.Object);
        services.AddScoped(_ => _bus.Object);
        services.AddScoped<PagamentoController>();

        var applicationAssembly = typeof(CriarPagamentoCommand).Assembly;
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);
            cfg.AddOpenBehavior(typeof(SharedKernel.Application.Behaviors.ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(SharedKernel.Application.Behaviors.TransactionBehavior<,>));
        });
        services.AddValidatorsFromAssembly(applicationAssembly);

        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    private async Task<PagamentoController> PrepararPendente(IServiceScope scope)
    {
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var criado = await sender.Send(new CriarPagamentoCommand(_ordemServicoId, _orcamentoId, _clienteId, 500m));
        Assert.True(criado.IsSuccess);
        _ordem.Clear();
        return scope.ServiceProvider.GetRequiredService<PagamentoController>();
    }

    [Fact(DisplayName = "Pagamento aprovado — sem IUnitOfWork, publica PagamentoAprovado depois de persistir")]
    public async Task Pagar_Aprovado_DevePublicarDepoisDePersistir()
    {
        // Arrange
        Assert.Empty(_provider.GetServices<IUnitOfWork>());
        using var scope = _provider.CreateScope();
        var controller = await PrepararPendente(scope);
        _processador
            .Setup(p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoProcessamentoPagamento(321, true, "accredited"));

        // Act
        var result = await controller.Pagar(_ordemServicoId, new PagarRequest("APRO"), _clienteId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Aprovado", result.Value.Status);
        Assert.Equal(["persistiu", "publicou-aprovado"], _ordem);
        _bus.Verify(b => b.Publish(
            It.Is<PagamentoAprovadoIntegrationEvent>(e =>
                e.PagamentoId == result.Value.PagamentoId
                && e.OrdemServicoId == _ordemServicoId
                && e.OrcamentoId == _orcamentoId),
            It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(b => b.Publish(It.IsAny<PagamentoRecusadoIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Pagamento recusado — publica PagamentoRecusado com o motivo, depois de persistir")]
    public async Task Pagar_Recusado_DevePublicarDepoisDePersistir()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        var controller = await PrepararPendente(scope);
        _processador
            .Setup(p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoProcessamentoPagamento(654, false, "cc_rejected_other_reason"));

        // Act
        var result = await controller.Pagar(_ordemServicoId, new PagarRequest("OTHE"), _clienteId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Recusado", result.Value.Status);
        Assert.Equal(["persistiu", "publicou-recusado"], _ordem);
        _bus.Verify(b => b.Publish(
            It.Is<PagamentoRecusadoIntegrationEvent>(e =>
                e.PagamentoId == result.Value.PagamentoId && e.Motivo == "cc_rejected_other_reason"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Pagamento por outro cliente — falha e não publica evento algum")]
    public async Task Pagar_OutroCliente_NaoDevePublicar()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        var controller = await PrepararPendente(scope);

        // Act
        var result = await controller.Pagar(_ordemServicoId, new PagarRequest("APRO"), Guid.NewGuid());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.AcessoNegado", result.Error.Code);
        Assert.Empty(_ordem);
        _bus.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Titular vazio — a validação rejeita antes de chamar o processador")]
    public async Task Pagar_TitularVazio_DeveRejeitarPelaValidacao()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        var controller = await PrepararPendente(scope);

        // Act
        var result = await controller.Pagar(_ordemServicoId, new PagarRequest(""), _clienteId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
        _processador.Verify(
            p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Gravação condicional perdida para outra chamada — falha com conflito e não publica evento algum")]
    public async Task Pagar_GravacaoCondicionalFalha_NaoDevePublicar()
    {
        // Arrange — enquanto esta chamada cobra no processador, outra já aprovou o mesmo pagamento
        using var scope = _provider.CreateScope();
        var controller = await PrepararPendente(scope);
        var repositorio = scope.ServiceProvider.GetRequiredService<IPagamentoRepository>();
        _processador
            .Setup(p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var atual = (await repositorio.ObterPorOrdemServicoEOrcamento(_ordemServicoId, _orcamentoId))!;
                await repositorio.Adicionar(atual with { Status = "Aprovado" });
                return new ResultadoProcessamentoPagamento(321, false, "cc_rejected_other_reason");
            });

        // Act
        var result = await controller.Pagar(_ordemServicoId, new PagarRequest("OTHE"), _clienteId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.ResolvidoPorOutraChamada", result.Error.Code);
        Assert.Empty(_ordem);
        _bus.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Cancelamento — pagamento Pendente vai a Cancelado, persiste e não publica evento")]
    public async Task Cancelar_Pendente_DeveCancelarSemPublicar()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        await PrepararPendente(scope);
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Act
        var result = await sender.Send(new CancelarPagamentoCommand(_ordemServicoId, _orcamentoId, "estoque insuficiente"));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(["persistiu"], _ordem);
        var gateway = scope.ServiceProvider.GetRequiredService<IPagamentoGateway>();
        var pagamento = Assert.Single(await gateway.ListarPorOrdemServico(_ordemServicoId));
        Assert.Equal(Domain.StatusPagamento.Cancelado, pagamento.Status);
        Assert.Equal("estoque insuficiente", pagamento.MotivoRecusa);
        _bus.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Listagem — dono vê seus pagamentos e outro cliente recebe acesso negado")]
    public async Task Listar_DeveRespeitarODono()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        var controller = await PrepararPendente(scope);

        // Act
        var doDono = await controller.ListarPorOrdemServico(_ordemServicoId, _clienteId);
        var daOficina = await controller.ListarPorOrdemServico(_ordemServicoId, null);
        var deOutro = await controller.ListarPorOrdemServico(_ordemServicoId, Guid.NewGuid());

        // Assert
        Assert.Single(doDono.Value);
        Assert.Single(daOficina.Value);
        Assert.True(deOutro.IsFailure);
        Assert.Equal("Pagamento.AcessoNegado", deOutro.Error.Code);
    }

    [Fact(DisplayName = "Evento OrcamentoAprovado reentregue — o pagamento é criado uma única vez")]
    public async Task CriarPagamento_Reentrega_DeveSerIdempotente()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var command = new CriarPagamentoCommand(_ordemServicoId, _orcamentoId, _clienteId, 500m);

        // Act
        var primeiro = await sender.Send(command);
        var segundo = await sender.Send(command);

        // Assert
        Assert.True(primeiro.IsSuccess);
        Assert.True(segundo.IsSuccess);
        Assert.Equal(primeiro.Value.Id, segundo.Value.Id);
        var gateway = scope.ServiceProvider.GetRequiredService<IPagamentoGateway>();
        Assert.Single(await gateway.ListarPorOrdemServico(_ordemServicoId));
    }

    /// <summary>Repositório em memória que registra, na ordem, quando uma atualização foi persistida.</summary>
    private sealed class RepositorioEmMemoria(List<string> ordem) : IPagamentoRepository
    {
        private readonly Dictionary<(Guid, Guid), PagamentoRecord> _dados = [];

        public Task Adicionar(PagamentoRecord record, CancellationToken ct = default)
        {
            _dados[(record.OrdemServicoId, record.OrcamentoId)] = record;
            return Task.CompletedTask;
        }

        public Task<bool> AtualizarSePendente(PagamentoRecord record, CancellationToken ct = default)
        {
            var chave = (record.OrdemServicoId, record.OrcamentoId);
            if (!_dados.TryGetValue(chave, out var atual) || atual.Status != "Pendente")
                return Task.FromResult(false);

            _dados[chave] = record;
            ordem.Add("persistiu");
            return Task.FromResult(true);
        }

        public Task<PagamentoRecord?> ObterPorOrdemServicoEOrcamento(Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default) =>
            Task.FromResult(_dados.GetValueOrDefault((ordemServicoId, orcamentoId)));

        public Task<IReadOnlyList<PagamentoRecord>> ListarPorOrdemServico(Guid ordemServicoId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PagamentoRecord>>(
                [.. _dados.Where(d => d.Key.Item1 == ordemServicoId).Select(d => d.Value)]);
    }
}
