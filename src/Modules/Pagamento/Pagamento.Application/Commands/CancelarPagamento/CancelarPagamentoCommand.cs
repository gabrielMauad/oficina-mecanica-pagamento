using SharedKernel.Application;

namespace Pagamento.Application.Commands.CancelarPagamento;

/// <summary>
/// Cancela o pagamento pendente do orçamento. Devolve <c>true</c> quando cancelou um pagamento existente
/// e <c>false</c> quando não havia o que cancelar agora (pagamento já resolvido, ou ainda inexistente:
/// nesse caso o cancelamento fica registrado e o pagamento nasce cancelado).
/// </summary>
public sealed record CancelarPagamentoCommand(
    Guid OrdemServicoId,
    Guid OrcamentoId,
    string Motivo
) : ICommand<bool>;
