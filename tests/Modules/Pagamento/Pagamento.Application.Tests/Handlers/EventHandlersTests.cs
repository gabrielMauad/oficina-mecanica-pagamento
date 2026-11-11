using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Application.Commands.CriarPagamento;
using Pagamento.Application.DomainEventHandlers;
using Pagamento.Application.IntegrationEventHandlers;
using Pagamento.Domain;
using Pagamento.Domain.Events;
using SharedKernel.Application;
using SharedKernel.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Tests.Handlers;

public class EventHandlersTests
{
    [Fact(DisplayName = "OrcamentoAprovado — dispara CriarPagamentoCommand com os dados do evento")]
    public async Task CriarPagamentoQuandoOrcamentoAprovado_DeveEnviarComando()
    {
        // Arrange
        var sender = new Mock<ISender>();
        CriarPagamentoCommand? enviado = null;
        sender
            .Setup(s => s.Send(It.IsAny<CriarPagamentoCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<PagamentoEntity>>, CancellationToken>((c, _) => enviado = (CriarPagamentoCommand)c)
            .ReturnsAsync(PagamentoEntity.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m));
        var handler = new CriarPagamentoQuandoOrcamentoAprovado(
            sender.Object, NullLogger<CriarPagamentoQuandoOrcamentoAprovado>.Instance);
        var evento = new OrcamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 899.90m);

        // Act
        await handler.Handle(evento, CancellationToken.None);

        // Assert
        Assert.NotNull(enviado);
        Assert.Equal(evento.OrdemServicoId, enviado.OrdemServicoId);
        Assert.Equal(evento.OrcamentoId, enviado.OrcamentoId);
        Assert.Equal(evento.ClienteId, enviado.ClienteId);
        Assert.Equal(evento.ValorTotal, enviado.Valor);
    }

    [Fact(DisplayName = "OrcamentoAprovado — falha do comando é registrada em log e não propaga")]
    public async Task CriarPagamentoQuandoOrcamentoAprovado_FalhaDoComando_NaoDeveLancar()
    {
        // Arrange
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(It.IsAny<CriarPagamentoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Validation("Pagamento.ValorInvalido", "Valor inválido."));
        var handler = new CriarPagamentoQuandoOrcamentoAprovado(
            sender.Object, NullLogger<CriarPagamentoQuandoOrcamentoAprovado>.Instance);
        var evento = new OrcamentoAprovadoIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m);

        // Act & Assert
        await handler.Handle(evento, CancellationToken.None);
        sender.Verify(s => s.Send(It.IsAny<CriarPagamentoCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "PagamentoAprovado — enfileira a publicação do evento de integração")]
    public async Task PublicarPagamentoAprovado_DeveEnfileirarPublicacao()
    {
        // Arrange
        var bus = new Mock<IIntegrationEventBus>();
        var pendentes = new PendingIntegrationEvents();
        var handler = new PublicarPagamentoAprovado(bus.Object, pendentes);
        var notificacao = new PagamentoAprovado(PagamentoId.Novo(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        // Act
        await handler.Handle(notificacao, CancellationToken.None);

        // Assert — nada é publicado até o TransactionBehavior executar a fila
        bus.Verify(b => b.Publish(It.IsAny<PagamentoAprovadoIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        var publicacao = Assert.Single(pendentes.GetPending());
        await publicacao(CancellationToken.None);
        bus.Verify(b => b.Publish(
            It.Is<PagamentoAprovadoIntegrationEvent>(e =>
                e.PagamentoId == notificacao.PagamentoId.Value
                && e.OrdemServicoId == notificacao.OrdemServicoId
                && e.OrcamentoId == notificacao.OrcamentoId
                && e.EventId != Guid.Empty),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "PagamentoRecusado — enfileira a publicação do evento de integração com o motivo")]
    public async Task PublicarPagamentoRecusado_DeveEnfileirarPublicacao()
    {
        // Arrange
        var bus = new Mock<IIntegrationEventBus>();
        var pendentes = new PendingIntegrationEvents();
        var handler = new PublicarPagamentoRecusado(bus.Object, pendentes);
        var notificacao = new PagamentoRecusado(
            PagamentoId.Novo(), Guid.NewGuid(), Guid.NewGuid(), "cc_rejected_other_reason", DateTime.UtcNow);

        // Act
        await handler.Handle(notificacao, CancellationToken.None);

        // Assert
        bus.Verify(b => b.Publish(It.IsAny<PagamentoRecusadoIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        var publicacao = Assert.Single(pendentes.GetPending());
        await publicacao(CancellationToken.None);
        bus.Verify(b => b.Publish(
            It.Is<PagamentoRecusadoIntegrationEvent>(e =>
                e.PagamentoId == notificacao.PagamentoId.Value
                && e.OrdemServicoId == notificacao.OrdemServicoId
                && e.OrcamentoId == notificacao.OrcamentoId
                && e.Motivo == "cc_rejected_other_reason"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
