using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Customers.Domain;

public sealed class Customer : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public string Email { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public string? Name { get; private set; }
    public bool IsRegistered { get; private set; }
    // Null for guests; set when the customer registers, or when a guest is promoted. Its
    // presence is what distinguishes "can log in" from "has merely bought here".
    public string? PasswordHash { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Customer() {} //EF

    private Customer(Guid tenantId, string email, string phone, string? name, bool isRegistered)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        // Normalised here as well as in every handler that looks one up: email is unique per
        // tenant, and the index is case-SENSITIVE. Without this, "Juan@x.cl" and "juan@x.cl"
        // become two customers for one person — which splits their order history and, worse,
        // gives them two independent anti-scalping counters.
        Email = email.ToLowerInvariant();
        Phone = phone;
        Name = name;
        IsRegistered = isRegistered;
    }

    public static Customer Guest(Guid tenantId, string email, string phone, string? name) =>
        new(tenantId, email, phone, name, isRegistered: false);

    public static Customer Registered(Guid tenantId, string email, string phone, string? name, string passwordHash)
    {
        var customer = new Customer(tenantId, email, phone, name, isRegistered: true);
        customer.PasswordHash = passwordHash;
        return customer;
    }

    // A guest who later registers keeps the same record — and with it their order history.
    //
    // ⚠️ Nothing here proves the registrant owns the address. Until email verification exists
    // (deferred with the rest of email delivery), knowing an email that once bought as a guest
    // is enough to claim that history. See the note in RegisterCustomer.
    public void PromoteToRegistered(string passwordHash)
    {
        PasswordHash = passwordHash;
        IsRegistered = true;
    }

    public void UpdateContact(string phone, string? name)
    {
        Phone = phone;
        Name = name;
    }
}