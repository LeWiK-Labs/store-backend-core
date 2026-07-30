using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Customers.Domain;
using LeWiK.Store.App.Platform;
using LeWiK.Store.App.Platform.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace LeWiK.Store.Api.Auth;

public static class AuthSchemes
{
    public const string Staff = "StaffSession";
    public const string Platform = "PlatformSession";
    public const string Customer = "CustomerSession";
}

// Three cookies for three populations. Distinct names are not cosmetic: they are what keeps a
// buyer's credential from ever being offered to a staff policy, on a host that serves both.
public static class AuthCookies
{
    public const string Staff = "lewik_panel_session";
    public const string Platform = "lewik_platform_session";
    public const string Customer = "lewik_store_session";
}

public static class AuthClaims
{
    public const string TenantId = "tenant_id";
    public const string SessionToken = "session_token";
}

// What a request needs to know about who is calling. Cached, so it must stay small and must
// not carry anything whose staleness would be dangerous for the length of the TTL.
public sealed record SessionPrincipal(Guid UserId, Guid? TenantId, string Role, string Name);

public abstract class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    StoreDbContext db, IDistributedCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected abstract string CookieName { get; }
    protected abstract SessionAudience Audience { get; }
    protected abstract Task<SessionPrincipal?> LoadAsync(string tokenHash);

    protected StoreDbContext Db => db;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[CookieName];
        // No cookie means anonymous, not rejected. NoResult lets unprotected endpoints keep
        // serving; Fail here would be wrong for every public route in the system.
        if (string.IsNullOrWhiteSpace(token))
            return AuthenticateResult.NoResult();

        var hash = OpaqueToken.Hash(token);
        var cacheKey = SessionCacheKeys.For(hash, Audience);

        SessionPrincipal? principal = null;
        var cached = await cache.GetStringAsync(cacheKey);
        if (cached is not null)
        {
            try { principal = JsonSerializer.Deserialize<SessionPrincipal>(cached); }
            catch (JsonException) { /* poisoned entry: fall through and reload from the database */ }
        }

        if (principal is null)
        {
            principal = await LoadAsync(hash);
            if (principal is null)
                return AuthenticateResult.Fail("Invalid or expired session.");

            // Short TTL. Revocation does not rely on it — logout deletes the key — but it
            // bounds the damage if an invalidation is ever missed, and it is what caps how
            // long an expired session could linger.
            await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(principal),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new(ClaimTypes.Name, principal.Name),
            new(ClaimTypes.Role, principal.Role),
            new(AuthClaims.SessionToken, token),
        };
        if (principal.TenantId is { } tenantId)
            claims.Add(new Claim(AuthClaims.TenantId, tenantId.ToString()));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

public sealed class StaffSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    StoreDbContext db, IDistributedCache cache)
    : SessionAuthenticationHandler(options, logger, encoder, db, cache)
{
    protected override string CookieName => AuthCookies.Staff;
    protected override SessionAudience Audience => SessionAudience.Staff;

    protected override async Task<SessionPrincipal?> LoadAsync(string tokenHash)
    {
        var now = DateTime.UtcNow;

        // IgnoreQueryFilters on the user side is required, not incidental: at authentication
        // time the tenant may not be pinned yet — the SESSION is what says which store this
        // is. The filter would compare against a tenant we have not established and find
        // nothing. Safety comes from the token hash being unguessable, and the tenant is then
        // checked explicitly by TenantMatchRequirement.
        //
        // Joining on IsActive also means deactivating a staff member kills their sessions:
        // the row stops loading, so the next request past the cache TTL fails.
        return await Db.Set<StaffSession>()
            .Where(s => s.TokenHash == tokenHash && s.RevokedAt == null && s.ExpiresAt > now)
            .Join(Db.Set<StaffUser>().IgnoreQueryFilters().Where(u => u.IsActive),
                s => s.StaffUserId, u => u.Id,
                (s, u) => new SessionPrincipal(u.Id, s.TenantId, u.Role.ToString(), u.Name))
            .FirstOrDefaultAsync();
    }
}

public sealed class PlatformSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    StoreDbContext db, IDistributedCache cache)
    : SessionAuthenticationHandler(options, logger, encoder, db, cache)
{
    protected override string CookieName => AuthCookies.Platform;
    protected override SessionAudience Audience => SessionAudience.Platform;

    protected override async Task<SessionPrincipal?> LoadAsync(string tokenHash)
    {
        var now = DateTime.UtcNow;
        return await Db.Set<PlatformSession>()
            .Where(s => s.TokenHash == tokenHash && s.RevokedAt == null && s.ExpiresAt > now)
            .Join(Db.Set<PlatformOperator>().Where(o => o.IsActive),
                s => s.PlatformOperatorId, o => o.Id,
                (s, o) => new SessionPrincipal(o.Id, null, "PlatformOperator", o.Name))
            .FirstOrDefaultAsync();
    }
}

public sealed class CustomerSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    StoreDbContext db, IDistributedCache cache)
    : SessionAuthenticationHandler(options, logger, encoder, db, cache)
{
    protected override string CookieName => AuthCookies.Customer;
    protected override SessionAudience Audience => SessionAudience.Customer;

    protected override async Task<SessionPrincipal?> LoadAsync(string tokenHash)
    {
        var now = DateTime.UtcNow;

        // IgnoreQueryFilters for the same reason as staff: the session is what names the store,
        // so filtering the customer by a tenant we have not established yet would find nothing.
        // The role is the constant "Customer" — buyers have no roles, and hardcoding it here is
        // what stops a customer principal from ever satisfying a staff role requirement.
        return await Db.Set<CustomerSession>()
            .Where(s => s.TokenHash == tokenHash && s.RevokedAt == null && s.ExpiresAt > now)
            .Join(Db.Set<Customer>().IgnoreQueryFilters(),
                s => s.CustomerId, c => c.Id,
                (s, c) => new SessionPrincipal(c.Id, s.TenantId, "Customer", c.Name ?? c.Email))
            .FirstOrDefaultAsync();
    }
}
