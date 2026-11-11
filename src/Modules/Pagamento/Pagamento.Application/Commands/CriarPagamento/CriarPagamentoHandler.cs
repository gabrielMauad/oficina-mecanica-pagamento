using MediatR;
using Microsoft.Extensions.Logging;
using Pagamento.Application.Gateways;
using SharedKernel.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Commands.CriarPagamento;

/// <summary>
/// Cria o pagamento <c>Pendente</c> de um orçamento aprovado. Idempotente: se já existe pagamento
/// da mesma ordem de serviço e orçamento (reentrega do evento), devolve o existente sem criar outro.
/// </summary>
public sealed class CriarPagamentoHandler : IRequestHandler<CriarPagamentoCommand, Result<PagamentoEntity>>
{
    private readonly IPagamentoGateway _gateway;
    private readonly ILogger<CriarPagamentoHandler> _logger;

    public CriarPagamentoHandler(IPagamentoGateway gateway, ILogger<CriarPagamentoHandler> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<PagamentoEntity>> Handle(CriarPagamentoCommand command, CancellationToken cancellationToken)
    {
        var existente = await _gateway.ObterPorOrdemServicoEOrcamento(
            command.OrdemServicoId, command.OrcamentoId, cancellationToken);
        if (existente is not null)
        {
            _logger.LogInformation(
                "Pagamento da ordem de serviço {OrdemServicoId} e orçamento {OrcamentoId} já existe ({Status}); evento ignorado.",
                command.OrdemServicoId, command.OrcamentoId, existente.Status);
            return existente;
        }

        var pagamentoResult = PagamentoEntity.Criar(
            command.OrdemServicoId, command.OrcamentoId, command.ClienteId, command.Valor);
        if (pagamentoResult.IsFailure)
            return pagamentoResult.Error;

        await _gateway.Adicionar(pagamentoResult.Value, cancellationToken);

        return pagamentoResult.Value;
    }
}
