using SharedKernel.Application;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Commands.PagarOrdemServico;

/// <param name="ClienteIdSolicitante">Id do cliente do token (claim <c>sub</c>); precisa ser o dono do pagamento.</param>
public sealed record PagarOrdemServicoCommand(
    Guid OrdemServicoId,
    string TitularCartao,
    Guid ClienteIdSolicitante
) : ICommand<PagamentoEntity>;
