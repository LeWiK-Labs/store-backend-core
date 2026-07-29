using Microsoft.AspNetCore.Identity;

namespace LeWiK.Store.App.Platform;

// Thin wrapper over ASP.NET's PBKDF2 hasher, which handles salt and iteration count itself.
// Only this slice of Identity is taken on purpose: the full stack would drag in its own user
// and store model plus migrations, which collide with everything already built here.
//
// The generic parameter is wrapped away so the rest of the codebase never has to name
// Identity's types — the hasher does not look at the subject, so a marker type is enough.
public sealed class PasswordHasher
{
    private sealed class Account;

    private readonly PasswordHasher<Account> _hasher = new();
    private static readonly Account Subject = new();

    public string Hash(string password) => _hasher.HashPassword(Subject, password);

    // Fails CLOSED on anything it cannot read. Identity's VerifyHashedPassword throws
    // FormatException on a hash that is not valid base64 — a truncated column, a row edited
    // by hand, a value written by something else — instead of returning Failed. Letting that
    // escape would turn one bad row into a 500 from the login endpoint rather than an
    // ordinary "wrong credentials", and a 500 is a far more interesting answer to an attacker.
    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash)) return false;

        try
        {
            return _hasher.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
