using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Contracts.IntegrationEvents;
using Pagamento.Application.Commands.CriarPagamento;
using SharedKernel.Application;

namespace Pagamento.Application.IntegrationEventHandlers;

public sealed class CriarPagamentoQuandoOrcamentoAprovado : IIntegrationEventHandler<OrcamentoAprovadoIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CriarPagamentoQuandoOrcamentoAprovado> _logger;

    public CriarPagamentoQuandoOrcamentoAprovado(
        ISender sender,
        ILogger<CriarPagamentoQuandoOrcamentoAprovado> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(OrcamentoAprovadoIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new CriarPagamentoCommand(
                integrationEvent.OrdemServicoId,
                integrationEvent.OrcamentoId,
                integrationEvent.ClienteId,
                integrationEvent.ValorTotal),
            ct);

        if (result.IsFailure)
        {
            _logger.LogError(
                "Falha ao criar o pagamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId}: {Erro}",
                integrationEvent.OrdemServicoId,
                integrationEvent.OrcamentoId,
                result.Error);
        }
    }
}
