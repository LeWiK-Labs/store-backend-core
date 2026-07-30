using LeWiK.Store.App.Payments.Domain;

namespace LeWiK.Tienda.Tests.Payments;

// A Refund resolves exactly once, the same rule Payment lives by. That rule is what makes a
// duplicate call — a retried handler, an admin double-click — safe: the second one is rejected
// by the entity instead of moving the number twice.
public class RefundTests
{
    private static Payment SucceededPayment(decimal amount = 33000m, string currency = "CLP")
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), PaymentGateway.Webpay,
            PaymentType.Full, amount, currency, externalReference: "tbk-token");
        payment.MarkSucceeded();
        return payment;
    }

    [Fact]
    public void New_refund_starts_pending_and_inherits_the_charge_it_targets()
    {
        var payment = SucceededPayment();

        var refund = new Refund(payment.TenantId, payment, 5000m, "Faltó una unidad");

        Assert.Equal(RefundState.Pending, refund.State);
        Assert.Equal(payment.Id, refund.PaymentId);
        Assert.Equal(payment.OrderId, refund.OrderId);
        Assert.Equal(payment.Currency, refund.Currency);   // never a currency of its own
        Assert.Equal(5000m, refund.Amount);
        Assert.Equal("Faltó una unidad", refund.Reason);
        Assert.Null(refund.ResolvedAt);
    }

    [Fact]
    public void Succeeding_stores_the_gateway_reference_and_stamps_the_resolution()
    {
        var refund = new Refund(Guid.NewGuid(), SucceededPayment(), 5000m, null);

        var result = refund.MarkSucceeded("REVERSED");

        Assert.True(result.IsSuccess);
        Assert.Equal(RefundState.Succeeded, refund.State);
        Assert.Equal("REVERSED", refund.ExternalReference);
        Assert.NotNull(refund.ResolvedAt);
    }

    [Fact]
    public void A_rejected_refund_keeps_why()
    {
        // Persisted rather than discarded: the attempt happened and the store needs to see it.
        var refund = new Refund(Guid.NewGuid(), SucceededPayment(), 5000m, null);

        refund.MarkFailed("status=rejected");

        Assert.Equal(RefundState.Failed, refund.State);
        Assert.Equal("status=rejected", refund.FailureReason);
        Assert.NotNull(refund.ResolvedAt);
    }

    [Fact]
    public void A_resolved_refund_cannot_be_resolved_again()
    {
        var refund = new Refund(Guid.NewGuid(), SucceededPayment(), 5000m, null);
        refund.MarkSucceeded("REVERSED");

        var second = refund.MarkSucceeded("NULLIFIED");

        Assert.True(second.IsFailure);
        Assert.Equal("refund.already_resolved", second.Error.Code);
        Assert.Equal("REVERSED", refund.ExternalReference);   // the first outcome stands
    }

    [Fact]
    public void A_failed_refund_cannot_be_flipped_to_succeeded()
    {
        // Retrying a rejected refund has to create a NEW Refund, so the rejected attempt stays
        // on the record instead of being overwritten by the retry.
        var refund = new Refund(Guid.NewGuid(), SucceededPayment(), 5000m, null);
        refund.MarkFailed("gateway said no");

        var result = refund.MarkSucceeded("REVERSED");

        Assert.True(result.IsFailure);
        Assert.Equal("refund.already_resolved", result.Error.Code);
        Assert.Equal(RefundState.Failed, refund.State);
    }
}
