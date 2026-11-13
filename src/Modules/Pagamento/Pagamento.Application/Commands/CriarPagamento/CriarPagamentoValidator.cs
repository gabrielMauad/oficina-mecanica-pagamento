using FluentValidation;

namespace Pagamento.Application.Commands.CriarPagamento;

public sealed class CriarPagamentoValidator : AbstractValidator<CriarPagamentoCommand>
{
    public CriarPagamentoValidator()
    {
        RuleFor(x => x.OrdemServicoId).NotEmpty().WithMessage("Id da ordem de serviço é obrigatório.");
        RuleFor(x => x.OrcamentoId).NotEmpty().WithMessage("Id do orçamento é obrigatório.");
        RuleFor(x => x.ClienteId).NotEmpty().WithMessage("Id do cliente é obrigatório.");
        RuleFor(x => x.Valor).GreaterThan(0).WithMessage("Valor do pagamento deve ser maior que zero.");
    }
}
