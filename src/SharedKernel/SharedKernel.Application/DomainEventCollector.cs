using SharedKernel.Domain;

namespace SharedKernel.Application;

public sealed class DomainEventCollector : IDomainEventCollector
{
    private readonly List<IHasDomainEvents> _agregados = [];

    public void Registrar(IHasDomainEvents agregado)
    {
        if (!_agregados.Contains(agregado))
            _agregados.Add(agregado);
    }

    public IReadOnlyList<IDomainEvent> Coletar() =>
        _agregados.SelectMany(a => a.DomainEvents).ToList();

    public void Limpar()
    {
        foreach (var agregado in _agregados)
            agregado.ClearDomainEvents();

        _agregados.Clear();
    }
}
