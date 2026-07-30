using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Platform.Domain;

// LeWiK staff. Not tenant-scoped, for the same reason Store is not: they create and manage
// stores across tenants, so a tenant filter would hide exactly what they exist to see.
public sealed class PlatformOperator : Entity, IAuditable
{
    public string Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PlatformOperator() { } // EF

    public PlatformOperator(string email, string name, string passwordHash)
    {
        Id = Guid.CreateVersion7();
        Email = email.ToLowerInvariant();
        Name = name;
        PasswordHash = passwordHash;
        IsActive = true;
    }

    public void SetPasswordHash(string hash) => PasswordHash = hash;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void RecordLogin() => LastLoginAt = DateTime.UtcNow;
}
