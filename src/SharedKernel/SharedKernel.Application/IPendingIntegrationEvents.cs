namespace SharedKernel.Application;

public interface IPendingIntegrationEvents
{
    void Enqueue(Func<CancellationToken, Task> publish);
    IReadOnlyList<Func<CancellationToken, Task>> GetPending();
}
