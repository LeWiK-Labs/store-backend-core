namespace LeWiK.Store.App.Common.Domain;

public interface IAuditable
{
    DateTime CreatedAt { get; }
    DateTime UpdatedAt { get; }
}