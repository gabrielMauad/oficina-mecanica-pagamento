using MediatR;
using SharedKernel.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Queries.ListarPagamentosPorOrdemServico;

/// <param name="ClienteIdSolicitante">
/// Id do cliente do token; <c>null</c> quando o solicitante é a oficina, que não tem restrição de dono.
/// </param>
public sealed record ListarPagamentosPorOrdemServicoQuery(
    Guid OrdemServicoId,
    Guid? ClienteIdSolicitante
) : IRequest<Result<IReadOnlyList<PagamentoEntity>>>;
