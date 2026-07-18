using System.Windows.Input;
using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Payments.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Payments;

public sealed record ConfigurePaymentMethodCommand(PaymentGateway Gateway, string credentialsJson)
    : ICommand<PaymentMethodResponse>;

public sealed record PaymentMethodResponse(PaymentGateway Gateway, bool IsActive);

public sealed class ConfigurePaymentMethodValidator : AbstractValidator<ConfigurePaymentMethodCommand>
{
    public ConfigurePaymentMethodValidator()
    {
        RuleFor(c => c.credentialsJson).NotEmpty();
    }
}

public sealed class ConfigurePaymentMethodCommandHandler(
    StoreDbContext db,
    ITenantContext tenant,
    CredentialProtector protector) : IRequestHandler<ConfigurePaymentMethodCommand, Result<PaymentMethodResponse>>
{
    public async Task<Result<PaymentMethodResponse>> Handle(ConfigurePaymentMethodCommand request, CancellationToken ct)
    {
        var encrypted = protector.Protect(request.credentialsJson);

        var config = await db.Set<PaymentMethodConfig>().FirstOrDefaultAsync(c => c.Gateway == request.Gateway, ct);

        if (config is null)
        {
            config = new PaymentMethodConfig(tenant.TenantId, request.Gateway, encrypted);
            db.Add(config);
        }
        else
        {
            config.UpdateCredentials(encrypted);
        }
        
        return new PaymentMethodResponse(config.Gateway, config.IsActive);
    }
}