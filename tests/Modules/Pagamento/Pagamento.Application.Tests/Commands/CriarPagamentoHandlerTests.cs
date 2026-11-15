using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pagamento.Application.Commands.CriarPagamento;
using Pagamento.Application.Gateways;
using Pagamento.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Tests.Commands;

public class CriarPagamentoHandlerTests
{
    private readonly Mock<IPagamentoGateway> _gatewayMock = new();
    private readonly CriarPagamentoHandler _handler;

    public CriarPagamentoHandlerTests()
    {
        _handler = new CriarPagamentoHandler(_gatewayMock.Object, NullLogger<CriarPagamentoHandler>.Instance);
    }

    private static CriarPagamentoCommand NovoCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 480.50m);

    [Fact(DisplayName = "Cenário feliz — cria o pagamento Pendente e persiste")]
    public async Task Handle_SemPagamentoExistente_DeveCriarPendente()
    {
        // Arrange
        var command = NovoCommand();
        _gatewayMock
            .Setup(g => g.ObterPorOrdemServicoEOrcamento(command.OrdemServicoId, command.OrcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(StatusPagamento.Pendente, result.Value.Status);
        Assert.Equal(command.OrdemServicoId, result.Value.OrdemServicoId);
        Assert.Equal(command.OrcamentoId, result.Value.OrcamentoId);
        Assert.Equal(command.ClienteId, result.Value.ClienteId);
        Assert.Equal(command.Valor, result.Value.Valor);
        _gatewayMock.Verify(
            g => g.Adicionar(It.Is<PagamentoEntity>(p => p.Id == result.Value.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "Idempotência — pagamento já existente é devolvido sem criar outro")]
    public async Task Handle_ComPagamentoExistente_DeveIgnorar()
    {
        // Arrange
        var command = NovoCommand();
        var existente = PagamentoEntity.Criar(command.OrdemServicoId, command.OrcamentoId, command.ClienteId, command.Valor).Value;
        existente.Aprovar(1);
        _gatewayMock
            .Setup(g => g.ObterPorOrdemServicoEOrcamento(command.OrdemServicoId, command.OrcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existente);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(existente, result.Value);
        Assert.Equal(StatusPagamento.Aprovado, result.Value.Status);
        _gatewayMock.Verify(g => g.Adicionar(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Cancelamento já registrado — cria o pagamento já Cancelado com o motivo, no lugar do registro, e nunca Pendente")]
    public async Task Handle_ComCancelamentoRegistrado_DeveCriarCancelado()
    {
        // Arrange
        var command = NovoCommand();
        _gatewayMock
            .Setup(g => g.ObterPorOrdemServicoEOrcamento(command.OrdemServicoId, command.OrcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoEntity?)null);
        _gatewayMock
            .Setup(g => g.ObterMotivoCancelamentoRegistrado(command.OrdemServicoId, command.OrcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Estoque insuficiente");
        _gatewayMock
            .Setup(g => g.SubstituirCancelamentoRegistrado(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(StatusPagamento.Cancelado, result.Value.Status);
        Assert.Equal("Estoque insuficiente", result.Value.MotivoRecusa);
        Assert.Equal(command.ClienteId, result.Value.ClienteId);
        Assert.Equal(command.Valor, result.Value.Valor);
        Assert.Empty(result.Value.DomainEvents);
        _gatewayMock.Verify(
            g => g.SubstituirCancelamentoRegistrado(
                It.Is<PagamentoEntity>(p => p.Id == result.Value.Id && p.Status == StatusPagamento.Cancelado),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _gatewayMock.Verify(g => g.Adicionar(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Cancelamento registrado, substituição perdida (reentrega já tratada) — devolve o pagamento existente")]
    public async Task Handle_SubstituicaoPerdida_DeveDevolverExistente()
    {
        // Arrange
        var command = NovoCommand();
        var cancelado = PagamentoEntity.Criar(command.OrdemServicoId, command.OrcamentoId, command.ClienteId, command.Valor).Value;
        cancelado.Cancelar("Estoque insuficiente");
        _gatewayMock
            .SetupSequence(g => g.ObterPorOrdemServicoEOrcamento(command.OrdemServicoId, command.OrcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoEntity?)null)
            .ReturnsAsync(cancelado);
        _gatewayMock
            .Setup(g => g.ObterMotivoCancelamentoRegistrado(command.OrdemServicoId, command.OrcamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Estoque insuficiente");
        _gatewayMock
            .Setup(g => g.SubstituirCancelamentoRegistrado(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(cancelado, result.Value);
    }

    [Fact(DisplayName = "Cancelamento registrado, substituição perdida e nenhum pagamento visível — devolve erro, sem criar Pendente")]
    public async Task Handle_SubstituicaoPerdidaSemExistente_DeveFalhar()
    {
        // Arrange
        var command = NovoCommand();
        _gatewayMock
            .Setup(g => g.ObterPorOrdemServicoEOrcamento(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoEntity?)null);
        _gatewayMock
            .Setup(g => g.ObterMotivoCancelamentoRegistrado(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Estoque insuficiente");
        _gatewayMock
            .Setup(g => g.SubstituirCancelamentoRegistrado(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.ResolvidoPorOutraChamada", result.Error.Code);
        _gatewayMock.Verify(g => g.Adicionar(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Erro de domínio — valor inválido não persiste")]
    public async Task Handle_ValorInvalido_DeveFalharSemPersistir()
    {
        // Arrange
        var command = NovoCommand() with { Valor = 0 };
        _gatewayMock
            .Setup(g => g.ObterPorOrdemServicoEOrcamento(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.ValorInvalido", result.Error.Code);
        _gatewayMock.Verify(g => g.Adicionar(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Validator — comando com campos inválidos é rejeitado")]
    public void Validator_CamposInvalidos_DeveRejeitar()
    {
        var validator = new CriarPagamentoValidator();

        var result = validator.Validate(new CriarPagamentoCommand(Guid.Empty, Guid.Empty, Guid.Empty, 0));

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
    }

    [Fact(DisplayName = "Validator — comando válido é aceito")]
    public void Validator_ComandoValido_DeveAceitar()
    {
        var validator = new CriarPagamentoValidator();

        Assert.True(validator.Validate(NovoCommand()).IsValid);
    }
}
