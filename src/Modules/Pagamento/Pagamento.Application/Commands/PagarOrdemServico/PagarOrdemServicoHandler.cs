using MediatR;
using Pagamento.Application.Gateways;
using Pagamento.Application.Gateways.Dtos;
using SharedKernel.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Commands.PagarOrdemServico;

/// <summary>
/// Cobra o pagamento pendente da ordem de serviço no processador e registra o resultado. O evento
/// de integração (aprovado ou recusado) sai do evento de domínio do agregado, publicado pelo
/// <c>TransactionBehavior</c> depois que o handler persiste.
/// </summary>
public sealed class PagarOrdemServicoHandler : IRequestHandler<PagarOrdemServicoCommand, Result<PagamentoEntity>>
{
    private readonly IPagamentoGateway _gateway;
    private readonly IProcessadorPagamentoGateway _processador;

    public PagarOrdemServicoHandler(IPagamentoGateway gateway, IProcessadorPagamentoGateway processador)
    {
        _gateway = gateway;
        _processador = processador;
    }

    public async Task<Result<PagamentoEntity>> Handle(PagarOrdemServicoCommand command, CancellationToken cancellationToken)
    {
        var pagamento = await _gateway.ObterPendentePorOrdemServico(command.OrdemServicoId, cancellationToken);
        if (pagamento is null)
            return PagamentoErrors.PendenteNaoEncontrado;
        if (pagamento.ClienteId != command.ClienteIdSolicitante)
            return PagamentoErrors.AcessoNegado;

        var resultadoProcessamento = await _processador.Processar(
            new SolicitacaoPagamento(
                pagamento.Valor,
                pagamento.OrcamentoId,
                $"Pagamento da ordem de serviço {pagamento.OrdemServicoId}",
                command.TitularCartao),
            cancellationToken);

        var transicao = resultadoProcessamento.Aprovado
            ? pagamento.Aprovar(resultadoProcessamento.PagamentoExternoId)
            : pagamento.Recusar(resultadoProcessamento.DetalheStatus, resultadoProcessamento.PagamentoExternoId);
        if (transicao.IsFailure)
            return transicao.Error;

        // Gravação condicional: se uma chamada concorrente já resolveu o pagamento, esta falha sem
        // registrar eventos, então nenhum evento de integração é publicado.
        if (!await _gateway.AtualizarSePendente(pagamento, cancellationToken))
            return PagamentoErrors.ResolvidoPorOutraChamada;

        return pagamento;
    }
}
