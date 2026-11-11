namespace SharedKernel.Domain;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OcorridoEm { get; }
}
