using System.Security.Claims;
using LeWiK.Store.App.Common.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace LeWiK.Store.Api.Auth;

// THE multi-tenant security check. A perfectly valid session for store A must not operate
// store B just because the request arrived on B's domain — otherwise any shop owner could run
// a competitor's store by changing the subdomain their browser points at.
//
// Written as an explicit comparison rather than leaning on the global query filter so that
// this check is a thing a reviewer can read, and so that failing it gives a diagnosable 403
// ("right credentials, wrong store") instead of results that silently come back empty.
public sealed class TenantMatchRequirement : IAuthorizationRequirement;

public sealed class TenantMatchHandler(ITenantContext tenant)
    : AuthorizationHandler<TenantMatchRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantMatchRequirement requirement)
    {
        var claim = context.User.FindFirstValue(AuthClaims.TenantId);

        // Fails closed on every missing piece: no claim, no resolved tenant, or a mismatch.
        // Not calling Succeed IS the rejection — there is no path that defaults to allow.
        if (Guid.TryParse(claim, out var sessionTenant)
            && tenant.HasTenant
            && sessionTenant == tenant.TenantId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
