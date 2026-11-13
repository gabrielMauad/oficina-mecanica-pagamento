namespace SharedKernel.Application;

public sealed class PendingIntegrationEvents : IPendingIntegrationEvents
{
    private readonly List<Func<CancellationToken, Task>> _pending = [];

    public void Enqueue(Func<CancellationToken, Task> publish) => _pending.Add(publish);

    /// <summary>
    /// Retorna os eventos pendentes e limpa a fila atomicamente.
    /// Isso evita que TransactionBehaviors aninhados (ex: command handler disparado
    /// por um integration event handler) re-publiquem os mesmos eventos.
    /// </summary>
    public IReadOnlyList<Func<CancellationToken, Task>> GetPending()
    {
        var snapshot = _pending.ToList();
        _pending.Clear();
        return snapshot;
    }
}
