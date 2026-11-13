using SharedKernel.Domain;

namespace SharedKernel.Application;

public interface IDomainEventCollector
{
    void Registrar(IHasDomainEvents agregado);
    IReadOnlyList<IDomainEvent> Coletar();
    void Limpar();
}
