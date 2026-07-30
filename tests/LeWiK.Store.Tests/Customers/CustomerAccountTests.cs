using LeWiK.Store.App.Customers.Domain;

namespace LeWiK.Tienda.Tests.Customers;

public class CustomerAccountTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();

    [Fact]
    public void A_guest_has_no_password_and_cannot_log_in()
    {
        // The presence of a hash is the whole difference between the two kinds of buyer.
        var guest = Customer.Guest(Tenant, "juan@test.cl", "+56911111111", "Juan");

        Assert.False(guest.IsRegistered);
        Assert.Null(guest.PasswordHash);
    }

    [Fact]
    public void Registering_a_guest_keeps_the_same_record()
    {
        // The identity that matters is the row: the order history hangs off this id, so
        // promotion must not mint a new one.
        var guest = Customer.Guest(Tenant, "juan@test.cl", "+56911111111", "Juan");
        var id = guest.Id;

        guest.PromoteToRegistered("hashed");

        Assert.Equal(id, guest.Id);
        Assert.True(guest.IsRegistered);
        Assert.Equal("hashed", guest.PasswordHash);
    }

    [Theory]
    [InlineData("Juan@Test.CL")]
    [InlineData("JUAN@TEST.CL")]
    [InlineData("juan@test.cl")]
    public void Emails_are_normalised_so_one_person_cannot_become_two_customers(string given)
    {
        // (TenantId, Email) is unique but compared case-sensitively, so without this each
        // spelling would be its own customer — with its own order history and, worse, its own
        // anti-scalping counter, since limits are enforced per customer id.
        var customer = Customer.Guest(Tenant, given, "+56911111111", null);

        Assert.Equal("juan@test.cl", customer.Email);
    }

    [Fact]
    public void A_registered_customer_is_born_with_its_hash()
    {
        var customer = Customer.Registered(Tenant, "Ana@Test.cl", "+56922222222", "Ana", "hashed");

        Assert.True(customer.IsRegistered);
        Assert.Equal("hashed", customer.PasswordHash);
        Assert.Equal("ana@test.cl", customer.Email);
    }

    [Fact]
    public void A_session_starts_live_and_can_be_revoked()
    {
        var session = new CustomerSession(Tenant, Guid.CreateVersion7(), "hash",
            DateTime.UtcNow.AddDays(30), "curl");

        Assert.Null(session.RevokedAt);

        session.Revoke();

        Assert.NotNull(session.RevokedAt);
    }

    [Fact]
    public void A_session_carries_the_store_it_was_issued_for()
    {
        // This is what TenantMatchRequirement compares against. Without it on the session, a
        // customer's cookie would work on any store's domain.
        var session = new CustomerSession(Tenant, Guid.CreateVersion7(), "hash",
            DateTime.UtcNow.AddDays(30), null);

        Assert.Equal(Tenant, session.TenantId);
    }
}
