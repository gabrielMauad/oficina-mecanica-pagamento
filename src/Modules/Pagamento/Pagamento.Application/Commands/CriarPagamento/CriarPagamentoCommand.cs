using SharedKernel.Application;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Commands.CriarPagamento;

public sealed record CriarPagamentoCommand(
    Guid OrdemServicoId,
    Guid OrcamentoId,
    Guid ClienteId,
    decimal Valor
) : ICommand<PagamentoEntity>;
