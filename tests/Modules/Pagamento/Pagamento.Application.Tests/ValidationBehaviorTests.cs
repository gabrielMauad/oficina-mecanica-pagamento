using FluentValidation;
using SharedKernel.Application.Behaviors;
using SharedKernel.Domain;

namespace Pagamento.Application.Tests;

public class ValidationBehaviorTests
{
    private sealed record Comando(string Nome);

    private sealed class NomeObrigatorio : AbstractValidator<Comando>
    {
        public NomeObrigatorio() => RuleFor(c => c.Nome).NotEmpty().WithMessage("Nome é obrigatório.");
    }

    private sealed class NomeComLimite : AbstractValidator<Comando>
    {
        public NomeComLimite() => RuleFor(c => c.Nome).MaximumLength(3).WithMessage("Nome muito longo.");
    }

    [Fact(DisplayName = "Sem validators — segue para o próximo handler")]
    public async Task SemValidators_DeveChamarNext()
    {
        var behavior = new ValidationBehavior<Comando, Result<string>>([]);

        var result = await behavior.Handle(
            new Comando(""), _ => Task.FromResult(Result.Success("ok")), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact(DisplayName = "Request inválido — devolve falha de validação sem chamar o próximo handler")]
    public async Task RequestInvalido_DeveRetornarFalhaSemChamarNext()
    {
        var chamouNext = false;
        var behavior = new ValidationBehavior<Comando, Result<string>>([new NomeObrigatorio()]);

        var result = await behavior.Handle(
            new Comando(""),
            _ => { chamouNext = true; return Task.FromResult(Result.Success("ok")); },
            TestContext.Current.CancellationToken);

        Assert.False(chamouNext);
        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
        Assert.Equal("Nome é obrigatório.", result.Error.Message);
    }

    [Fact(DisplayName = "Dois validators e apenas um falha — a mensagem aparece uma única vez")]
    public async Task DoisValidators_UmFalha_MensagemApareceUmaVez()
    {
        var behavior = new ValidationBehavior<Comando, Result<string>>([new NomeObrigatorio(), new NomeComLimite()]);
        var invertido = new ValidationBehavior<Comando, Result<string>>([new NomeComLimite(), new NomeObrigatorio()]);

        var result = await behavior.Handle(
            new Comando(""), _ => Task.FromResult(Result.Success("ok")), TestContext.Current.CancellationToken);
        var resultInvertido = await invertido.Handle(
            new Comando(""), _ => Task.FromResult(Result.Success("ok")), TestContext.Current.CancellationToken);

        Assert.Equal("Nome é obrigatório.", result.Error.Message);
        Assert.Equal("Nome é obrigatório.", resultInvertido.Error.Message);
    }
}
