using SharedKernel.Domain;

namespace Pagamento.Domain.Tests;

/// <summary>Igualdade por identidade (Entity) e por valor (ValueObject) do SharedKernel.</summary>
public class SharedKernelEqualityTests
{
    private sealed class EntidadeA(Guid id) : Entity<Guid>(id);

    private sealed class EntidadeB(Guid id) : Entity<Guid>(id);

    private sealed class Dinheiro(decimal valor, string? moeda) : ValueObject
    {
        protected override IEnumerable<object?> GetComponents()
        {
            yield return valor;
            yield return moeda;
        }
    }

    private sealed class Outro(decimal valor, string? moeda) : ValueObject
    {
        protected override IEnumerable<object?> GetComponents()
        {
            yield return valor;
            yield return moeda;
        }
    }

    [Fact(DisplayName = "Entity — mesmo tipo e mesmo Id são iguais, com o mesmo hash")]
    public void Entity_MesmoIdMesmoTipo_DeveSerIgual()
    {
        var id = Guid.NewGuid();
        var a1 = new EntidadeA(id);
        var a2 = new EntidadeA(id);

        Assert.True(a1.Equals(a2));
        Assert.True(a1 == a2);
        Assert.False(a1 != a2);
        Assert.Equal(a1.GetHashCode(), a2.GetHashCode());
    }

    [Fact(DisplayName = "Entity — Ids diferentes, tipos diferentes, nulo e outro objeto são diferentes")]
    public void Entity_Diferentes_NaoDeveSerIgual()
    {
        var id = Guid.NewGuid();
        var a = new EntidadeA(id);

        Assert.False(a.Equals(new EntidadeA(Guid.NewGuid())));
        Assert.False(a.Equals(new EntidadeB(id)));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals("não é entidade"));
        Assert.True(a != new EntidadeA(Guid.NewGuid()));
    }

    [Fact(DisplayName = "Entity — a mesma instância é igual a si mesma e dois nulos são iguais")]
    public void Entity_MesmaInstanciaENulos()
    {
        var a = new EntidadeA(Guid.NewGuid());
        EntidadeA? nulo1 = null;
        EntidadeA? nulo2 = null;

        Assert.True(a.Equals(a));
        Assert.True(nulo1 == nulo2);
        Assert.False(a == nulo1);
        Assert.False(nulo1 == a);
    }

    [Fact(DisplayName = "ValueObject — mesmos componentes são iguais, com o mesmo hash")]
    public void ValueObject_MesmosComponentes_DeveSerIgual()
    {
        var v1 = new Dinheiro(10m, "BRL");
        var v2 = new Dinheiro(10m, "BRL");

        Assert.True(v1.Equals(v2));
        Assert.True(v1 == v2);
        Assert.False(v1 != v2);
        Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
    }

    [Fact(DisplayName = "ValueObject — componentes diferentes, tipos diferentes e nulo são diferentes")]
    public void ValueObject_Diferentes_NaoDeveSerIgual()
    {
        var v = new Dinheiro(10m, "BRL");

        Assert.False(v.Equals(new Dinheiro(11m, "BRL")));
        Assert.False(v.Equals(new Dinheiro(10m, "USD")));
        Assert.False(v.Equals(new Outro(10m, "BRL")));
        Assert.False(v.Equals(null));
        Assert.True(v != new Dinheiro(11m, "BRL"));
    }

    [Fact(DisplayName = "ValueObject — componente nulo participa da igualdade e do hash")]
    public void ValueObject_ComponenteNulo()
    {
        var v1 = new Dinheiro(10m, null);
        var v2 = new Dinheiro(10m, null);
        Dinheiro? nulo1 = null;
        Dinheiro? nulo2 = null;

        Assert.True(v1 == v2);
        Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        Assert.False(v1 == new Dinheiro(10m, "BRL"));
        Assert.True(nulo1 == nulo2);
        Assert.False(v1 == nulo1);
    }
}
