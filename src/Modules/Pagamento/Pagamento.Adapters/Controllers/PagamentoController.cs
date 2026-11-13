using MediatR;
using Pagamento.Adapters.Models.Requests;
using Pagamento.Adapters.Models.ViewModels;
using Pagamento.Adapters.Presenters;
using Pagamento.Application.Commands.PagarOrdemServico;
using Pagamento.Application.Queries.ListarPagamentosPorOrdemServico;
using SharedKernel.Domain;

namespace Pagamento.Adapters.Controllers;

public sealed class PagamentoController
{
    private readonly ISender _sender;

    public PagamentoController(ISender sender) => _sender = sender;

    /// <param name="clienteIdSolicitante">Id do cliente do token, ou <c>null</c> para a oficina.</param>
    public async Task<Result<List<PagamentoViewModel>>> ListarPorOrdemServico(Guid ordemServicoId, Guid? clienteIdSolicitante)
    {
        var result = await _sender.Send(new ListarPagamentosPorOrdemServicoQuery(ordemServicoId, clienteIdSolicitante));
        if (result.IsFailure) return result.Error;
        return PagamentoPresenter.PresentListar(result.Value);
    }

    public async Task<Result<PagamentoViewModel>> Pagar(Guid ordemServicoId, PagarRequest request, Guid clienteIdSolicitante)
    {
        var result = await _sender.Send(
            new PagarOrdemServicoCommand(ordemServicoId, request.TitularCartao, clienteIdSolicitante));
        if (result.IsFailure) return result.Error;
        return PagamentoPresenter.Present(result.Value);
    }
}
