using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.Api.Tenancy;

public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenant)
    {
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var raw) && Guid.TryParse(raw, out var tenantId))
        {
            tenant.SetTenant(tenantId);
        }
        
        await next(context);
    }
}