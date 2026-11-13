using SharedKernel.Application;

namespace Pagamento.Application.Commands.CancelarPagamento;

/// <summary>
/// Cancela o pagamento pendente do orçamento. Devolve <c>true</c> quando cancelou e <c>false</c> quando
/// não havia o que cancelar (pagamento inexistente ou já resolvido).
/// </summary>
public sealed record CancelarPagamentoCommand(
    Guid OrdemServicoId,
    Guid OrcamentoId,
    string Motivo
) : ICommand<bool>;
