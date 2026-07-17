using Microsoft.AspNetCore.DataProtection;

namespace LeWiK.Store.App.Payments;

public sealed class CredentialProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Lewik.Payments.Credentials");
    
    public string Protect(string credentialsJson) => _protector.Protect(credentialsJson);
    public string Unprotect(string encrypted) => _protector.Unprotect(encrypted);
}