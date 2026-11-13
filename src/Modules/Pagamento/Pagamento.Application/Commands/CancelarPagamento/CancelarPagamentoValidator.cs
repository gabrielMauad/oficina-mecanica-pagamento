using FluentValidation;

namespace Pagamento.Application.Commands.CancelarPagamento;

public sealed class CancelarPagamentoValidator : AbstractValidator<CancelarPagamentoCommand>
{
    public CancelarPagamentoValidator()
    {
        RuleFor(x => x.OrdemServicoId).NotEmpty().WithMessage("Id da ordem de serviço é obrigatório.");
        RuleFor(x => x.OrcamentoId).NotEmpty().WithMessage("Id do orçamento é obrigatório.");
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Motivo do cancelamento é obrigatório.");
    }
}
