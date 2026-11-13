namespace SharedKernel.Domain;

public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType()) return false;
        return GetComponents().SequenceEqual(((ValueObject)obj).GetComponents());
    }

    public override int GetHashCode() =>
        GetComponents().Aggregate(0, (hash, component) =>
            HashCode.Combine(hash, component?.GetHashCode() ?? 0));

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
