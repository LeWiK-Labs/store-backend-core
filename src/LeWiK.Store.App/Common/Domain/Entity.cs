namespace LeWiK.Store.App.Common.Domain;

public abstract class Entity
{
    public Guid Id { get; protected init; }

    public override bool Equals(object? obj) => obj is Entity other && GetType() == other.GetType() && Id == other.Id && Id != default;
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}