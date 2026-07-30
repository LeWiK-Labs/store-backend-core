using LeWiK.Store.App.Payments.Gateways;

namespace LeWiK.Tienda.Tests.Payments;

// The abort/timeout return has no token_ws; we recover the payment from TBK_ORDEN_COMPRA,
// which echoes the buyOrder we sent (ShortId of the payment id). Encode and decode must be
// exact inverses, or the buyer's cancellation would resolve to the wrong payment (or none).
public class WebpayBuyOrderTests
{
    [Fact]
    public void ShortId_and_TryDecodeBuyOrder_round_trip()
    {
        for (var i = 0; i < 1000; i++)
        {
            var id = Guid.NewGuid();
            var buyOrder = WebpayGatewayClient.ShortId(id);

            Assert.True(buyOrder.Length <= 26);                     // Webpay's buyOrder cap
            Assert.True(WebpayGatewayClient.TryDecodeBuyOrder(buyOrder, out var back));
            Assert.Equal(id, back);
        }
    }

    [Fact]
    public void ShortId_is_url_safe_and_unpadded()
    {
        var buyOrder = WebpayGatewayClient.ShortId(Guid.NewGuid());
        Assert.DoesNotContain('+', buyOrder);
        Assert.DoesNotContain('/', buyOrder);
        Assert.DoesNotContain('=', buyOrder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64-!!!")]
    [InlineData("dG9vLXNob3J0")]   // valid base64 but decodes to fewer than 16 bytes
    public void TryDecodeBuyOrder_rejects_garbage(string input)
    {
        Assert.False(WebpayGatewayClient.TryDecodeBuyOrder(input, out var id));
        Assert.Equal(Guid.Empty, id);
    }

    [Fact]
    public void TryDecodeBuyOrder_rejects_null()
    {
        Assert.False(WebpayGatewayClient.TryDecodeBuyOrder(null, out var id));
        Assert.Equal(Guid.Empty, id);
    }
}
