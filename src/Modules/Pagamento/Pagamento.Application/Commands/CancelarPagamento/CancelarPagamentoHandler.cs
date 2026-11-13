using MediatR;
using Microsoft.Extensions.Logging;
using Pagamento.Application.Gateways;
using Pagamento.Domain;
using SharedKernel.Domain;

namespace Pagamento.Application.Commands.CancelarPagamento;

/// <summary>
/// Cancela o pagamento <c>Pendente</c> de um orçamento que deixou de valer, sem publicar evento. É
/// tolerante a reentrega e ao caso comum de não haver pagamento: nesses casos só registra log e
/// devolve <c>false</c>, sem erro.
/// </summary>
public sealed class CancelarPagamentoHandler : IRequestHandler<CancelarPagamentoCommand, Result<bool>>
{
    private readonly IPagamentoGateway _gateway;
    private readonly ILogger<CancelarPagamentoHandler> _logger;

    public CancelarPagamentoHandler(IPagamentoGateway gateway, ILogger<CancelarPagamentoHandler> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(CancelarPagamentoCommand command, CancellationToken cancellationToken)
    {
        var pagamento = await _gateway.ObterPorOrdemServicoEOrcamento(
            command.OrdemServicoId, command.OrcamentoId, cancellationToken);
        if (pagamento is null)
        {
            _logger.LogInformation(
                "Não há pagamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId}; nada a cancelar.",
                command.OrdemServicoId, command.OrcamentoId);
            return false;
        }

        if (pagamento.Status != StatusPagamento.Pendente)
        {
            _logger.LogInformation(
                "Pagamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId} está {Status}; cancelamento ignorado.",
                command.OrdemServicoId, command.OrcamentoId, pagamento.Status);
            return false;
        }

        var transicao = pagamento.Cancelar(command.Motivo);
        if (transicao.IsFailure)
            return transicao.Error;

        if (!await _gateway.AtualizarSePendente(pagamento, cancellationToken))
        {
            _logger.LogWarning(
                "Pagamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId} foi resolvido por outra chamada antes do cancelamento; cancelamento ignorado.",
                command.OrdemServicoId, command.OrcamentoId);
            return false;
        }

        _logger.LogInformation(
            "Pagamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId} cancelado: {Motivo}",
            command.OrdemServicoId, command.OrcamentoId, command.Motivo);
        return true;
    }
}
