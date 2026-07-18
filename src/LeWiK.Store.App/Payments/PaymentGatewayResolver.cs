using LeWiK.Store.App.Payments.Domain;

namespace LeWiK.Store.App.Payments;

public sealed class PaymentGatewayResolver(IEnumerable<IPaymentGatewayClient> clients)
{
    public IPaymentGatewayClient? For(PaymentGateway gateway) => clients.FirstOrDefault(c => c.Gateway == gateway);
}