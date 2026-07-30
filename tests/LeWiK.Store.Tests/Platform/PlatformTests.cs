using LeWiK.Store.App.Platform;
using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Platform.Domain;
// Every namespace here nests under LeWiK, which already has a member namespace called Store,
// and enclosing-namespace members beat compilation-unit aliases in name lookup — so even
// `using Store = ...Domain.Store;` loses. It needs a name that does not collide at all.
using StoreEntity = LeWiK.Store.App.Platform.Domain.Store;
using ITenantScoped = LeWiK.Store.App.Common.Tenancy.ITenantScoped;

namespace LeWiK.Tienda.Tests.Platform;

public class StoreTests
{
    [Fact]
    public void A_new_store_is_active_and_has_its_own_id()
    {
        // That Id is what every other entity in the system carries as TenantId.
        var store = new StoreEntity("Card Shop", "cardshop");

        Assert.NotEqual(Guid.Empty, store.Id);
        Assert.Equal(StoreStatus.Active, store.Status);
        Assert.True(store.IsActive);
    }

    [Theory]
    [InlineData("CardShop", "cardshop")]
    [InlineData("CARDSHOP", "cardshop")]
    public void Slugs_are_lowercased_because_hostnames_are(string given, string expected)
    {
        // Two spellings of one hostname must never become two stores.
        var store = new StoreEntity("Card Shop", given);

        Assert.Equal(expected, store.Slug);
    }

    [Fact]
    public void Custom_domain_is_lowercased_and_optional()
    {
        Assert.Null(new StoreEntity("Card Shop", "cardshop").CustomDomain);
        Assert.Equal("tienda.cl", new StoreEntity("Card Shop", "cardshop", "Tienda.CL").CustomDomain);
        Assert.Equal("x.cl", new StoreEntity("s", "s").Let(s => { s.SetCustomDomain("X.CL"); return s; }).CustomDomain);
    }

    [Fact]
    public void Suspend_and_activate_flip_the_only_entitlement_gate_we_have()
    {
        var store = new StoreEntity("Card Shop", "cardshop");

        store.Suspend();
        Assert.False(store.IsActive);
        Assert.Equal(StoreStatus.Suspended, store.Status);

        store.Activate();
        Assert.True(store.IsActive);
    }
}

// Tiny helper so the fluent assertion above stays readable.
internal static class TestExtensions
{
    public static T Let<T>(this T subject, Func<T, T> apply) => apply(subject);
}

public class StaffUserTests
{
    private static StaffUser Staff(StaffRole role = StaffRole.Staff) =>
        new(Guid.NewGuid(), "Persona@Tienda.CL", "Persona", "hash", role);

    [Fact]
    public void A_new_staff_user_is_active_with_a_normalised_email()
    {
        var user = Staff();

        Assert.Equal("persona@tienda.cl", user.Email);   // logins are case-insensitive
        Assert.True(user.IsActive);
        Assert.Null(user.LastLoginAt);
    }

    [Fact]
    public void Deactivating_does_not_erase_the_person()
    {
        // Deactivation is reversible and keeps the record: their orders and stock movements
        // still have to point at somebody.
        var user = Staff();

        user.Deactivate();
        Assert.False(user.IsActive);

        user.Activate();
        Assert.True(user.IsActive);
    }

    [Fact]
    public void Role_can_be_changed_without_touching_the_password()
    {
        var user = Staff(StaffRole.Staff);

        user.ChangeRole(StaffRole.Cashier);

        Assert.Equal(StaffRole.Cashier, user.Role);
        Assert.Equal("hash", user.PasswordHash);
    }

    [Fact]
    public void Cashier_exists_now_so_phase_5_does_not_reopen_every_policy()
    {
        Assert.Equal(4, Enum.GetValues<StaffRole>().Length);
        Assert.Contains(StaffRole.Cashier, Enum.GetValues<StaffRole>());
        Assert.Contains(StaffRole.Owner, Enum.GetValues<StaffRole>());
    }

    [Fact]
    public void Recording_a_login_stamps_the_time()
    {
        var user = Staff();

        user.RecordLogin();

        Assert.NotNull(user.LastLoginAt);
    }
}

public class PlatformOperatorTests
{
    [Fact]
    public void An_operator_is_active_with_a_normalised_email()
    {
        var op = new PlatformOperator("Admin@LeWiK.cl", "Platform Admin", "hash");

        Assert.Equal("admin@lewik.cl", op.Email);
        Assert.True(op.IsActive);
    }

    [Fact]
    public void An_operator_carries_no_tenant()
    {
        // The type has no TenantId at all: operators work across stores by construction,
        // not by being granted an exception.
        Assert.DoesNotContain(typeof(PlatformOperator).GetProperties(), p => p.Name == "TenantId");
        Assert.False(typeof(ITenantScoped)
            .IsAssignableFrom(typeof(PlatformOperator)));
    }
}

public class TenancyShapeTests
{
    [Fact]
    public void Store_is_the_tenant_so_it_is_not_tenant_scoped()
    {
        // If Store were ITenantScoped the global filter would apply to it, which is circular:
        // you would need a tenant to find the thing that defines the tenant. It would also
        // hide every store from the operators whose job is to see them all.
        Assert.False(typeof(ITenantScoped).IsAssignableFrom(typeof(StoreEntity)));
    }

    [Fact]
    public void Staff_belongs_to_a_store_so_it_is_tenant_scoped()
    {
        Assert.True(typeof(ITenantScoped).IsAssignableFrom(typeof(StaffUser)));
    }
}

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void A_hash_is_not_the_password()
    {
        var hash = _hasher.Hash("password-larga-123");

        Assert.NotEqual("password-larga-123", hash);
        Assert.DoesNotContain("password-larga-123", hash);
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // Per-hash salt: two people with the same password must not share a hash, or the
        // database tells you who to attack together.
        Assert.NotEqual(_hasher.Hash("password-larga-123"), _hasher.Hash("password-larga-123"));
    }

    [Fact]
    public void Verify_accepts_the_right_password_and_rejects_everything_else()
    {
        var hash = _hasher.Hash("password-larga-123");

        Assert.True(_hasher.Verify(hash, "password-larga-123"));
        Assert.False(_hasher.Verify(hash, "password-larga-124"));
        Assert.False(_hasher.Verify(hash, "PASSWORD-LARGA-123"));
        Assert.False(_hasher.Verify(hash, ""));
    }

    [Theory]
    [InlineData("not-a-real-hash")]     // not base64 at all
    [InlineData("AQAAAA==")]            // valid base64, truncated payload
    [InlineData("")]
    public void Verify_fails_closed_on_a_hash_it_cannot_read(string hash)
    {
        // Identity throws FormatException here rather than returning Failed. Unwrapped, one
        // corrupted row would turn the login endpoint into a 500 instead of a plain rejection.
        Assert.False(_hasher.Verify(hash, "password-larga-123"));
    }

    [Fact]
    public void The_hash_fits_the_column()
    {
        Assert.True(_hasher.Hash(new string('x', 128)).Length <= 256);   // HasMaxLength(256)
    }
}
