using LeWiK.Store.App.Orders;
using LeWiK.Store.App.Orders.Domain;

namespace LeWiK.Tienda.Tests.Orders;

// The link is the only credential a guest has, so what makes it stop working matters as much
// as what makes it work. These cover both directions.
public class PaymentLinkTests
{
    private static readonly DateTime Now = new(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);

    private static OrderLineDraft Line(int qty, decimal price, bool preorder = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), $"SKU-{price}", "Item", price, "CLP", qty, preorder);

    private static Order DepositedOrder()
    {
        // 20000 total, 6000 deposit paid -> 14000 still owed. The preorder case the link exists for.
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(2, 10000, preorder: true)], 6000).Value;
        order.ApplyPayment(6000);
        return order;
    }

    [Fact]
    public void A_fresh_link_is_valid_until_it_expires()
    {
        var order = DepositedOrder();

        order.SetPaymentLink("hash-a", Now.AddDays(30));

        Assert.True(order.IsPaymentLinkValid(Now));
        Assert.True(order.IsPaymentLinkValid(Now.AddDays(29)));
        Assert.False(order.IsPaymentLinkValid(Now.AddDays(31)));
    }

    [Fact]
    public void Expiry_is_exclusive_at_the_boundary()
    {
        var order = DepositedOrder();
        var expiry = Now.AddDays(30);

        order.SetPaymentLink("hash-a", expiry);

        Assert.False(order.IsPaymentLinkValid(expiry));
        Assert.True(order.IsPaymentLinkValid(expiry.AddSeconds(-1)));
    }

    [Fact]
    public void An_order_with_no_link_is_never_valid()
    {
        var order = DepositedOrder();

        Assert.False(order.IsPaymentLinkValid(Now));
        Assert.Null(order.PaymentLinkHash);
    }

    [Fact]
    public void Issuing_a_new_link_replaces_the_previous_hash()
    {
        // This is the revocation story: the old token no longer hashes to what is stored,
        // so it stops resolving the moment a new one is issued.
        var order = DepositedOrder();
        order.SetPaymentLink("hash-old", Now.AddDays(30));

        order.SetPaymentLink("hash-new", Now.AddDays(30));

        Assert.Equal("hash-new", order.PaymentLinkHash);
    }

    [Fact]
    public void Revoking_clears_the_link_entirely()
    {
        var order = DepositedOrder();
        order.SetPaymentLink("hash-a", Now.AddDays(30));

        order.RevokePaymentLink();

        Assert.False(order.IsPaymentLinkValid(Now));
        Assert.Null(order.PaymentLinkHash);
        Assert.Null(order.PaymentLinkExpiresAt);
    }

    [Fact]
    public void A_cancelled_order_stops_honouring_a_live_link()
    {
        // The link was already sent when the order got cancelled — it is out there, unexpired,
        // in someone's chat. Without this the guest could still be charged for a dead order.
        var order = DepositedOrder();
        order.SetPaymentLink("hash-a", Now.AddDays(30));

        order.Cancel();

        Assert.False(order.IsPaymentLinkValid(Now));
        Assert.NotNull(order.PaymentLinkHash);   // not revoked, just not honoured
    }
}

public class PaymentLinkTokenTests
{
    [Fact]
    public void Tokens_are_url_safe_and_unique()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => PaymentLinkTokens.Generate()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            // Travels in a URL path segment: nothing here may need escaping.
            Assert.DoesNotContain('+', t);
            Assert.DoesNotContain('/', t);
            Assert.DoesNotContain('=', t);
            Assert.Equal(43, t.Length);   // 32 bytes, base64url, unpadded
        });
    }

    [Fact]
    public void Hashing_is_stable_so_a_link_resolves_on_every_click()
    {
        var token = PaymentLinkTokens.Generate();

        Assert.Equal(PaymentLinkTokens.Hash(token), PaymentLinkTokens.Hash(token));
    }

    [Fact]
    public void The_hash_fits_the_column_and_does_not_leak_the_token()
    {
        var token = PaymentLinkTokens.Generate();

        var hash = PaymentLinkTokens.Hash(token);

        Assert.Equal(64, hash.Length);            // matches HasMaxLength(64)
        Assert.DoesNotContain(token, hash);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Different_tokens_hash_differently()
    {
        Assert.NotEqual(
            PaymentLinkTokens.Hash(PaymentLinkTokens.Generate()),
            PaymentLinkTokens.Hash(PaymentLinkTokens.Generate()));
    }
}
