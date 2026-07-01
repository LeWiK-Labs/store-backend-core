using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Orders.Domain;

public sealed record OrderPlaced(Guid TenantId, Guid OrderId, Guid CustomerId) : IDomainEvent;
public sealed record OrderDeposited(Guid TenantId, Guid OrderId, Guid CustomerId) : IDomainEvent;
public sealed record OrderPaid(Guid TenantId, Guid OrderId, Guid CustomerId) : IDomainEvent;
public sealed record OrderDelivered(Guid TenantId, Guid OrderId, Guid CustomerId) : IDomainEvent;
public sealed record OrderCancelled(Guid TenantId, Guid OrderId, Guid CustomerId) : IDomainEvent;