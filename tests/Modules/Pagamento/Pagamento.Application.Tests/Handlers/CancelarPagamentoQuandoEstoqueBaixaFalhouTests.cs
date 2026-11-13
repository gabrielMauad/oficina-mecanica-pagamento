using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Application.Commands.CancelarPagamento;
using Pagamento.Application.IntegrationEventHandlers;
using SharedKernel.Domain;

namespace Pagamento.Application.Tests.Handlers;

public class CancelarPagamentoQuandoEstoqueBaixaFalhouTests
{
    private static EstoqueBaixaFalhouIntegrationEvent NovoEvento() =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "Estoque insuficiente");

    [Fact(DisplayName = "EstoqueBaixaFalhou — dispara CancelarPagamentoCommand com os dados do evento")]
    public async Task DeveEnviarComando()
    {
        // Arrange
        var sender = new Mock<ISender>();
        CancelarPagamentoCommand? enviado = null;
        sender
            .Setup(s => s.Send(It.IsAny<CancelarPagamentoCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<bool>>, CancellationToken>((c, _) => enviado = (CancelarPagamentoCommand)c)
            .ReturnsAsync(true);
        var handler = new CancelarPagamentoQuandoEstoqueBaixaFalhou(
            sender.Object, NullLogger<CancelarPagamentoQuandoEstoqueBaixaFalhou>.Instance);
        var evento = NovoEvento();

        // Act
        await handler.Handle(evento, CancellationToken.None);

        // Assert
        Assert.NotNull(enviado);
        Assert.Equal(evento.OrdemServicoId, enviado.OrdemServicoId);
        Assert.Equal(evento.OrcamentoId, enviado.OrcamentoId);
        Assert.Equal(evento.Motivo, enviado.Motivo);
    }

    [Fact(DisplayName = "EstoqueBaixaFalhou — falha do comando é registrada em log e não propaga")]
    public async Task FalhaDoComando_NaoDeveLancar()
    {
        // Arrange
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(It.IsAny<CancelarPagamentoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Validation("validation.failed", "Motivo do cancelamento é obrigatório."));
        var handler = new CancelarPagamentoQuandoEstoqueBaixaFalhou(
            sender.Object, NullLogger<CancelarPagamentoQuandoEstoqueBaixaFalhou>.Instance);

        // Act
        await handler.Handle(NovoEvento(), CancellationToken.None);

        // Assert
        sender.Verify(s => s.Send(It.IsAny<CancelarPagamentoCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
