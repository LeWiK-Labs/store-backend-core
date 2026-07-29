using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Platform.Domain;

// Same shape as StaffSession minus the tenant, because an operator does not belong to a store.
// A separate table rather than a nullable column on one: two populations that must never be
// confused are easier to keep separate when they cannot physically be the same row.
public sealed class PlatformSession : Entity, IAuditable
{
    public Guid PlatformOperatorId { get; private init; }
    public string TokenHash { get; private init; } = null!;
    public DateTime ExpiresAt { get; private init; }
    public DateTime? RevokedAt { get; private set; }
    public string? UserAgent { get; private init; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PlatformSession() { } // EF

    public PlatformSession(Guid operatorId, string tokenHash, DateTime expiresAt, string? userAgent)
    {
        Id = Guid.CreateVersion7();
        PlatformOperatorId = operatorId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        UserAgent = userAgent;
    }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke() => RevokedAt = DateTime.UtcNow;
}
