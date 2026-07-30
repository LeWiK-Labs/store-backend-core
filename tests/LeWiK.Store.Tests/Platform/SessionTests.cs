using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Platform;
using LeWiK.Store.App.Platform.Domain;

namespace LeWiK.Tienda.Tests.Platform;

public class SessionTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    private static StaffSession Staff(DateTime? expiresAt = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "hash", expiresAt ?? Now.AddHours(12), "curl/8");

    [Fact]
    public void A_fresh_session_is_active_until_it_expires()
    {
        var session = Staff();

        Assert.True(session.IsActive(Now));
        Assert.True(session.IsActive(Now.AddHours(11)));
        Assert.False(session.IsActive(Now.AddHours(13)));
    }

    [Fact]
    public void Expiry_is_exclusive_at_the_boundary()
    {
        var expiry = Now.AddHours(12);
        var session = Staff(expiry);

        Assert.False(session.IsActive(expiry));
        Assert.True(session.IsActive(expiry.AddSeconds(-1)));
    }

    [Fact]
    public void Revoking_beats_a_still_valid_expiry()
    {
        // This is what logout leans on: the clock still says fine, the session says no.
        var session = Staff();

        session.Revoke();

        Assert.False(session.IsActive(Now));
        Assert.NotNull(session.RevokedAt);
    }

    [Fact]
    public void Revoking_twice_is_harmless()
    {
        // Logout is idempotent, so a double-click must not throw.
        var session = Staff();
        session.Revoke();
        var first = session.RevokedAt;

        session.Revoke();

        Assert.False(session.IsActive(Now));
        Assert.NotNull(first);
    }

    [Fact]
    public void A_platform_session_carries_no_tenant()
    {
        // Operators are not of a store, so the type has nowhere to put one. That is what
        // makes it impossible for a platform session to satisfy a store policy by accident.
        Assert.DoesNotContain(typeof(PlatformSession).GetProperties(), p => p.Name == "TenantId");
    }

    [Fact]
    public void Sessions_are_not_tenant_scoped_so_they_are_found_by_token_alone()
    {
        // Deliberate: the lookup happens before a tenant is established — the session is what
        // reveals which store. The tenant is then compared explicitly in the policy.
        Assert.False(typeof(LeWiK.Store.App.Common.Tenancy.ITenantScoped)
            .IsAssignableFrom(typeof(StaffSession)));
    }
}

public class SessionCacheKeyTests
{
    [Fact]
    public void No_two_populations_share_a_cache_key()
    {
        // If any two collided, one population's session could be served from another's cache
        // entry — a buyer's token resolving to a staff principal, or worse.
        var hash = OpaqueToken.Hash("same-token");

        var keys = Enum.GetValues<SessionAudience>()
            .Select(a => SessionCacheKeys.For(hash, a))
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void The_key_is_derived_from_the_hash_not_the_token()
    {
        // Redis must never hold anything that could be replayed as a credential.
        var token = OpaqueToken.Generate();

        var key = SessionCacheKeys.For(OpaqueToken.Hash(token), SessionAudience.Staff);

        Assert.DoesNotContain(token, key);
        Assert.Contains(OpaqueToken.Hash(token), key);
    }

    [Fact]
    public void Logout_can_compute_the_same_key_the_handler_cached_under()
    {
        // The whole instant-revocation story depends on these agreeing.
        var token = OpaqueToken.Generate();

        Assert.Equal(SessionCacheKeys.For(OpaqueToken.Hash(token), SessionAudience.Customer),
                     SessionCacheKeys.For(OpaqueToken.Hash(token), SessionAudience.Customer));
    }
}

public class OpaqueTokenTests
{
    [Fact]
    public void Tokens_are_unique_and_url_safe()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => OpaqueToken.Generate()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.Equal(43, t.Length);          // 32 bytes, base64url, unpadded
            Assert.DoesNotContain('+', t);
            Assert.DoesNotContain('/', t);
            Assert.DoesNotContain('=', t);
        });
    }

    [Fact]
    public void The_hash_fits_the_column_and_hides_the_token()
    {
        var token = OpaqueToken.Generate();

        var hash = OpaqueToken.Hash(token);

        Assert.Equal(64, hash.Length);           // matches HasMaxLength(64)
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain(token, hash);
    }

    [Fact]
    public void Payment_links_and_sessions_share_one_implementation()
    {
        // One model for every bearer credential: if these ever diverged, one of the two would
        // quietly stop being the thing that was reviewed.
        var token = OpaqueToken.Generate();

        Assert.Equal(OpaqueToken.Hash(token), LeWiK.Store.App.Orders.PaymentLinkTokens.Hash(token));
    }
}

public class LoginTimingTests
{
    [Fact]
    public void Rejecting_an_unknown_account_costs_the_same_as_a_wrong_password()
    {
        // The login handler returns one indistinguishable error either way; this makes sure
        // the CLOCK does not give away what the message hides. Without SpendVerificationTime
        // the unknown-account path skips PBKDF2 entirely and answers far faster, which is an
        // account-enumeration oracle.
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("password-larga-123");

        // Warm up: first PBKDF2 call pays JIT and table setup.
        hasher.Verify(hash, "wrong");
        hasher.SpendVerificationTime();

        var wrongPassword = Time(() => hasher.Verify(hash, "password-larga-124"));
        var unknownAccount = Time(hasher.SpendVerificationTime);

        // Generous bound: this asserts the decoy path does real work, not that it is
        // cycle-identical. Skipping the hash entirely would be orders of magnitude faster.
        var ratio = (double)unknownAccount / Math.Max(wrongPassword, 1);
        Assert.InRange(ratio, 0.2, 5.0);
    }

    private static long Time(Action action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10; i++) action();
        return sw.ElapsedMilliseconds;
    }
}
