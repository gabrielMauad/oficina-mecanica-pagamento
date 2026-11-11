using MediatR;
using Pagamento.Application.Gateways;
using SharedKernel.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Queries.ListarPagamentosPorOrdemServico;

public sealed class ListarPagamentosPorOrdemServicoHandler
    : IRequestHandler<ListarPagamentosPorOrdemServicoQuery, Result<IReadOnlyList<PagamentoEntity>>>
{
    private readonly IPagamentoGateway _gateway;

    public ListarPagamentosPorOrdemServicoHandler(IPagamentoGateway gateway) => _gateway = gateway;

    public async Task<Result<IReadOnlyList<PagamentoEntity>>> Handle(
        ListarPagamentosPorOrdemServicoQuery request, CancellationToken cancellationToken)
    {
        var pagamentos = await _gateway.ListarPorOrdemServico(request.OrdemServicoId, cancellationToken);

        // Todos os pagamentos de uma ordem de serviço pertencem ao mesmo cliente.
        if (request.ClienteIdSolicitante is { } clienteId && pagamentos.Any(p => p.ClienteId != clienteId))
            return PagamentoErrors.AcessoNegado;

        return Result.Success(pagamentos);
    }
}
