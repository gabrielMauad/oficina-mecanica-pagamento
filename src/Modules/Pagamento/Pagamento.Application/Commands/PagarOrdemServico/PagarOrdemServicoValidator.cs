using FluentValidation;

namespace Pagamento.Application.Commands.PagarOrdemServico;

public sealed class PagarOrdemServicoValidator : AbstractValidator<PagarOrdemServicoCommand>
{
    public PagarOrdemServicoValidator()
    {
        RuleFor(x => x.OrdemServicoId).NotEmpty().WithMessage("Id da ordem de serviço é obrigatório.");
        RuleFor(x => x.ClienteIdSolicitante).NotEmpty().WithMessage("Cliente solicitante é obrigatório.");
        RuleFor(x => x.TitularCartao)
            .NotEmpty().WithMessage("Titular do cartão é obrigatório.")
            .MaximumLength(100).WithMessage("Titular do cartão deve ter no máximo 100 caracteres.");
    }
}
