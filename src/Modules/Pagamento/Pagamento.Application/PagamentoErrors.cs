using SharedKernel.Domain;

namespace Pagamento.Application;

internal static class PagamentoErrors
{
    public static readonly Error PendenteNaoEncontrado = Error.NotFound(
        "Pagamento.PendenteNaoEncontrado",
        "Não há pagamento pendente para a ordem de serviço informada.");

    public static readonly Error AcessoNegado = Error.Forbidden(
        "Pagamento.AcessoNegado",
        "O pagamento pertence a outro cliente.");
}
