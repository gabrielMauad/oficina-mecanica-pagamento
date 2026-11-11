namespace Pagamento.Domain;

public sealed record PagamentoId(Guid Value)
{
    public static PagamentoId Novo() => new(Guid.NewGuid());
}
