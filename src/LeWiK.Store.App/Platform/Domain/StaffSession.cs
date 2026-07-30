using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Platform.Domain;

// A logged-in staff member's session.
//
// Deliberately NOT ITenantScoped. It is found by token hash — unguessable, so the lookup needs
// no tenant to be safe — and the tenant is then compared explicitly in the authorization
// policy. That is the point: the multi-tenant check for sessions should be a line of code a
// reviewer can find, not a filter that silently applies and silently might not.
public sealed class StaffSession : Entity, IAuditable
{
    public Guid TenantId { get; private init; }
    public Guid StaffUserId { get; private init; }
    public string TokenHash { get; private init; } = null!;
    public DateTime ExpiresAt { get; private init; }
    public DateTime? RevokedAt { get; private set; }
    public string? UserAgent { get; private init; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private StaffSession() { } // EF

    public StaffSession(Guid tenantId, Guid staffUserId, string tokenHash, DateTime expiresAt, string? userAgent)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        StaffUserId = staffUserId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        UserAgent = userAgent;
    }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke() => RevokedAt = DateTime.UtcNow;
}
