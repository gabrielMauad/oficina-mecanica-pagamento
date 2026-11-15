using MediatR;
using Microsoft.Extensions.Logging;
using Pagamento.Application.Gateways;
using Pagamento.Domain;
using SharedKernel.Domain;

namespace Pagamento.Application.Commands.CancelarPagamento;

/// <summary>
/// Cancela o pagamento <c>Pendente</c> de um orçamento que deixou de valer, sem publicar evento. É
/// tolerante a reentrega e ao pagamento já resolvido: nesses casos só registra log e devolve
/// <c>false</c>, sem erro.
/// <para>
/// Os eventos de aprovação e de falha de estoque chegam por filas distintas, sem ordem entre si. Se o
/// cancelamento chega antes de existir pagamento, o cancelamento é registrado para a chave do
/// orçamento (gravação condicional à chave estar livre) e a criação posterior, que consulta esse
/// registro, nasce <c>Cancelado</c> em vez de <c>Pendente</c>. Se a criação vence a disputa pela
/// chave, o pagamento recém-criado é relido e cancelado normalmente.
/// </para>
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
            if (await _gateway.RegistrarCancelamento(
                    command.OrdemServicoId, command.OrcamentoId, command.Motivo, cancellationToken))
            {
                _logger.LogInformation(
                    "Ainda não há pagamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId}; cancelamento registrado para que o pagamento nasça cancelado: {Motivo}",
                    command.OrdemServicoId, command.OrcamentoId, command.Motivo);
                return false;
            }

            // A chave foi ocupada entre a leitura e a gravação: ou por um pagamento (a criação venceu a
            // corrida) ou por um registro de cancelamento (reentrega).
            pagamento = await _gateway.ObterPorOrdemServicoEOrcamento(
                command.OrdemServicoId, command.OrcamentoId, cancellationToken);
            if (pagamento is null)
            {
                _logger.LogInformation(
                    "Cancelamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId} já estava registrado; evento ignorado.",
                    command.OrdemServicoId, command.OrcamentoId);
                return false;
            }
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
