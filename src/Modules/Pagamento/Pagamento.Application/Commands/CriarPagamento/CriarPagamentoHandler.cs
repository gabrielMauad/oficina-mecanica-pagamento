using MediatR;
using Microsoft.Extensions.Logging;
using Pagamento.Application.Gateways;
using SharedKernel.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Commands.CriarPagamento;

/// <summary>
/// Cria o pagamento <c>Pendente</c> de um orçamento aprovado. Idempotente: se já existe pagamento
/// da mesma ordem de serviço e orçamento (reentrega do evento), devolve o existente sem criar outro.
/// <para>
/// Se o cancelamento do orçamento (falha da baixa de estoque) chegou antes deste evento, ele está
/// registrado para a chave do orçamento: o pagamento é então criado já <c>Cancelado</c>, com o motivo
/// registrado, e nunca <c>Pendente</c>. Se o cancelamento for registrado entre a consulta e a gravação,
/// a gravação condicional deste handler falha e o evento é reprocessado pelo retry da mensageria, que
/// passa a encontrar o registro.
/// </para>
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
        var pagamento = pagamentoResult.Value;

        var motivoCancelamento = await _gateway.ObterMotivoCancelamentoRegistrado(
            command.OrdemServicoId, command.OrcamentoId, cancellationToken);
        if (motivoCancelamento is not null)
            return await CriarJaCancelado(command, pagamento, motivoCancelamento, cancellationToken);

        await _gateway.Adicionar(pagamento, cancellationToken);

        return pagamento;
    }

    private async Task<Result<PagamentoEntity>> CriarJaCancelado(
        CriarPagamentoCommand command, PagamentoEntity pagamento, string motivo, CancellationToken cancellationToken)
    {
        var transicao = pagamento.Cancelar(motivo);
        if (transicao.IsFailure)
            return transicao.Error;

        if (!await _gateway.SubstituirCancelamentoRegistrado(pagamento, cancellationToken))
        {
            // Outra entrega do mesmo evento já trocou o registro pelo pagamento cancelado.
            var existente = await _gateway.ObterPorOrdemServicoEOrcamento(
                command.OrdemServicoId, command.OrcamentoId, cancellationToken);
            return existente is not null ? existente : PagamentoErrors.ResolvidoPorOutraChamada;
        }

        _logger.LogInformation(
            "O orçamento {OrcamentoId} da ordem de serviço {OrdemServicoId} já havia sido cancelado ({Motivo}); pagamento criado como Cancelado.",
            command.OrcamentoId, command.OrdemServicoId, motivo);
        return pagamento;
    }
}
