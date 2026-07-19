using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Payments.Domain;

public enum PaymentGateway {Transfer, Webpay, MercadoPago, Manual}

public sealed class PaymentMethodConfig : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public PaymentGateway Gateway { get; private init; }
    public string EncryptedCredentials { get; private set; } = null!; //JSON, encrypted at rest
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    
    private PaymentMethodConfig() {} //EF

    public PaymentMethodConfig(Guid tenantId, PaymentGateway gateway, string encryptedCredentials)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Gateway = gateway;
        EncryptedCredentials = encryptedCredentials;
        IsActive = true;
    }

    public void UpdateCredentials(string encryptedCredentials) => EncryptedCredentials = encryptedCredentials;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}