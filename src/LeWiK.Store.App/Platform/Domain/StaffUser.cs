using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Platform.Domain;

// Owner: the store's owner (billing, gateways). Admin: full operation.
// Staff: day to day (stock, orders). Cashier: the POS and nothing else.
//
// Cashier is defined now even though the POS is Phase 5: modelling it here costs nothing,
// while adding a role later forces a re-reading of every policy written in between.
public enum StaffRole { Owner, Admin, Staff, Cashier }

// A person who works at a store and logs into its panel.
public sealed class StaffUser : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public string Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public StaffRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private StaffUser() { } // EF

    public StaffUser(Guid tenantId, string email, string name, string passwordHash, StaffRole role)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Email = email.ToLowerInvariant();
        Name = name;
        PasswordHash = passwordHash;
        Role = role;
        IsActive = true;
    }

    public void SetPasswordHash(string hash) => PasswordHash = hash;
    public void ChangeRole(StaffRole role) => Role = role;
    public void Deactivate() => IsActive = false;   // existing sessions are killed separately (4.2)
    public void Activate() => IsActive = true;
    public void RecordLogin() => LastLoginAt = DateTime.UtcNow;
}
