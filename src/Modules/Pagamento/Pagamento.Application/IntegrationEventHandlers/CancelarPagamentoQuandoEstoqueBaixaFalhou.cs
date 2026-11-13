using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Application.Commands.CancelarPagamento;
using SharedKernel.Application;

namespace Pagamento.Application.IntegrationEventHandlers;

/// <summary>
/// Quando a baixa de estoque do orçamento falha, a ordem de serviço rejeita o orçamento. Se o cliente
/// já o tinha aprovado (pagamento <c>Pendente</c> criado), o pagamento é cancelado para não ser cobrado
/// um serviço que não será executado.
/// </summary>
public sealed class CancelarPagamentoQuandoEstoqueBaixaFalhou : IIntegrationEventHandler<EstoqueBaixaFalhouIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelarPagamentoQuandoEstoqueBaixaFalhou> _logger;

    public CancelarPagamentoQuandoEstoqueBaixaFalhou(
        ISender sender,
        ILogger<CancelarPagamentoQuandoEstoqueBaixaFalhou> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(EstoqueBaixaFalhouIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new CancelarPagamentoCommand(
                integrationEvent.OrdemServicoId,
                integrationEvent.OrcamentoId,
                integrationEvent.Motivo),
            ct);

        if (result.IsFailure)
        {
            _logger.LogError(
                "Falha ao cancelar o pagamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId}: {Erro}",
                integrationEvent.OrdemServicoId,
                integrationEvent.OrcamentoId,
                result.Error);
        }
    }
}
