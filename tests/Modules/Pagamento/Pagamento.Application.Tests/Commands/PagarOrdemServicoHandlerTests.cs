using Moq;
using Pagamento.Application.Commands.PagarOrdemServico;
using Pagamento.Application.Gateways;
using Pagamento.Application.Gateways.Dtos;
using Pagamento.Domain;
using Pagamento.Domain.Events;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Tests.Commands;

public class PagarOrdemServicoHandlerTests
{
    private readonly Mock<IPagamentoGateway> _gatewayMock = new();
    private readonly Mock<IProcessadorPagamentoGateway> _processadorMock = new();
    private readonly PagarOrdemServicoHandler _handler;

    private readonly Guid _ordemServicoId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();

    public PagarOrdemServicoHandlerTests()
    {
        _handler = new PagarOrdemServicoHandler(_gatewayMock.Object, _processadorMock.Object);
    }

    private PagamentoEntity PendenteDaOrdem(decimal valor = 250m)
    {
        var pagamento = PagamentoEntity.Criar(_ordemServicoId, Guid.NewGuid(), _clienteId, valor).Value;
        _gatewayMock
            .Setup(g => g.ObterPendentePorOrdemServico(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);
        return pagamento;
    }

    private void ProcessadorResponde(ResultadoProcessamentoPagamento resultado) =>
        _processadorMock
            .Setup(p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultado);

    [Fact(DisplayName = "Pagamento aprovado — marca Aprovado, persiste e levanta PagamentoAprovado")]
    public async Task Handle_ProcessadorAprova_DeveAprovar()
    {
        // Arrange
        var pagamento = PendenteDaOrdem(250m);
        ProcessadorResponde(new ResultadoProcessamentoPagamento(777, true, "accredited"));

        // Act
        var result = await _handler.Handle(
            new PagarOrdemServicoCommand(_ordemServicoId, "APRO", _clienteId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(pagamento, result.Value);
        Assert.Equal(StatusPagamento.Aprovado, result.Value.Status);
        Assert.Equal(777, result.Value.MercadoPagoPaymentId);
        Assert.Contains(pagamento.DomainEvents, e => e is PagamentoAprovado);
        _gatewayMock.Verify(g => g.Atualizar(pagamento, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Pagamento recusado — marca Recusado com o motivo, persiste e levanta PagamentoRecusado")]
    public async Task Handle_ProcessadorRecusa_DeveRecusar()
    {
        // Arrange
        var pagamento = PendenteDaOrdem();
        ProcessadorResponde(new ResultadoProcessamentoPagamento(888, false, "cc_rejected_other_reason"));

        // Act
        var result = await _handler.Handle(
            new PagarOrdemServicoCommand(_ordemServicoId, "OTHE", _clienteId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(StatusPagamento.Recusado, result.Value.Status);
        Assert.Equal("cc_rejected_other_reason", result.Value.MotivoRecusa);
        Assert.Equal(888, result.Value.MercadoPagoPaymentId);
        var evento = Assert.IsType<PagamentoRecusado>(Assert.Single(pagamento.DomainEvents));
        Assert.Equal("cc_rejected_other_reason", evento.Motivo);
        _gatewayMock.Verify(g => g.Atualizar(pagamento, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Cobrança — envia ao processador o valor, o orçamento, a descrição e o titular")]
    public async Task Handle_DeveEnviarSolicitacaoAoProcessador()
    {
        // Arrange
        var pagamento = PendenteDaOrdem(123.45m);
        SolicitacaoPagamento? enviada = null;
        _processadorMock
            .Setup(p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()))
            .Callback<SolicitacaoPagamento, CancellationToken>((s, _) => enviada = s)
            .ReturnsAsync(new ResultadoProcessamentoPagamento(1, true, "accredited"));

        // Act
        await _handler.Handle(new PagarOrdemServicoCommand(_ordemServicoId, "APRO", _clienteId), CancellationToken.None);

        // Assert
        Assert.NotNull(enviada);
        Assert.Equal(123.45m, enviada.Valor);
        Assert.Equal(pagamento.OrcamentoId, enviada.OrcamentoId);
        Assert.Equal("APRO", enviada.TitularCartao);
        Assert.Contains(_ordemServicoId.ToString(), enviada.Descricao);
    }

    [Fact(DisplayName = "Erro 404 — sem pagamento pendente da ordem de serviço")]
    public async Task Handle_SemPendente_DeveRetornarNaoEncontrado()
    {
        // Arrange
        _gatewayMock
            .Setup(g => g.ObterPendentePorOrdemServico(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoEntity?)null);

        // Act
        var result = await _handler.Handle(
            new PagarOrdemServicoCommand(_ordemServicoId, "APRO", _clienteId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.PendenteNaoEncontrado", result.Error.Code);
        _processadorMock.Verify(
            p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Erro 403 — outro cliente não paga e o processador não é chamado")]
    public async Task Handle_OutroCliente_DeveRetornarAcessoNegado()
    {
        // Arrange
        var pagamento = PendenteDaOrdem();

        // Act
        var result = await _handler.Handle(
            new PagarOrdemServicoCommand(_ordemServicoId, "APRO", Guid.NewGuid()), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.AcessoNegado", result.Error.Code);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        _processadorMock.Verify(
            p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _gatewayMock.Verify(g => g.Atualizar(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Falha técnica no processador — propaga, mantém Pendente e não persiste")]
    public async Task Handle_ProcessadorFalha_DevePropagarSemAlterarPagamento()
    {
        // Arrange
        var pagamento = PendenteDaOrdem();
        _processadorMock
            .Setup(p => p.Processar(It.IsAny<SolicitacaoPagamento>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("processador fora do ar"));

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => _handler.Handle(
            new PagarOrdemServicoCommand(_ordemServicoId, "APRO", _clienteId), CancellationToken.None));
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        Assert.Empty(pagamento.DomainEvents);
        _gatewayMock.Verify(g => g.Atualizar(It.IsAny<PagamentoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Validator — titular vazio ou muito longo e ids vazios são rejeitados")]
    public void Validator_ComandoInvalido_DeveRejeitar()
    {
        var validator = new PagarOrdemServicoValidator();

        Assert.False(validator.Validate(new PagarOrdemServicoCommand(Guid.NewGuid(), "", Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(new PagarOrdemServicoCommand(Guid.NewGuid(), new string('A', 101), Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(new PagarOrdemServicoCommand(Guid.Empty, "APRO", Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(new PagarOrdemServicoCommand(Guid.NewGuid(), "APRO", Guid.Empty)).IsValid);
    }

    [Fact(DisplayName = "Validator — comando válido é aceito")]
    public void Validator_ComandoValido_DeveAceitar()
    {
        var validator = new PagarOrdemServicoValidator();

        Assert.True(validator.Validate(new PagarOrdemServicoCommand(Guid.NewGuid(), "APRO", Guid.NewGuid())).IsValid);
    }
}
