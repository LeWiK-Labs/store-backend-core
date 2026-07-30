namespace LeWiK.Store.App.Common.Security;

// The three populations that can hold a session. Separate namespaces for the cache as well as
// separate tables and cookies: one token hash must never resolve to a principal of the wrong
// kind, even by collision or by a bug in whoever writes the key.
public enum SessionAudience { Staff, Platform, Customer }

public static class SessionCacheKeys
{
    public static string For(string tokenHash, SessionAudience audience) =>
        $"session:{audience.ToString().ToLowerInvariant()}:{tokenHash}";
}
