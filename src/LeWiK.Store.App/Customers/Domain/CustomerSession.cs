using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Customers.Domain;

// Same pattern as staff sessions: found by token hash, tenant checked in the policy.
//
// Deliberately NOT ITenantScoped, like the other two session tables. At authentication time
// the tenant may not be pinned yet — the session is what says which store this is — so a
// query filter here would compare against a tenant we have not established. The token hash
// is the unguessable part; the store is then checked explicitly by TenantMatchRequirement.
public sealed class CustomerSession : Entity, IAuditable
{
    public Guid TenantId { get; private init; }
    public Guid CustomerId { get; private init; }
    public string TokenHash { get; private init; } = null!;
    public DateTime ExpiresAt { get; private init; }
    public DateTime? RevokedAt { get; private set; }
    public string? UserAgent { get; private init; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private CustomerSession() { } // EF

    public CustomerSession(Guid tenantId, Guid customerId, string tokenHash, DateTime expiresAt, string? userAgent)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CustomerId = customerId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        UserAgent = userAgent;
    }

    public void Revoke() => RevokedAt = DateTime.UtcNow;
}
