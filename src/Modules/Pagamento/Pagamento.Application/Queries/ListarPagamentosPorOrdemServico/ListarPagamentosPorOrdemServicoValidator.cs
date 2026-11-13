using FluentValidation;

namespace Pagamento.Application.Queries.ListarPagamentosPorOrdemServico;

public sealed class ListarPagamentosPorOrdemServicoValidator : AbstractValidator<ListarPagamentosPorOrdemServicoQuery>
{
    public ListarPagamentosPorOrdemServicoValidator()
    {
        RuleFor(x => x.OrdemServicoId).NotEmpty().WithMessage("Id da ordem de serviço é obrigatório.");
    }
}
