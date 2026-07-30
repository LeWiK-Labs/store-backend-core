using LeWiK.Store.App.Common.Results;

namespace LeWiK.Store.App.Customers;

public static class CustomerErrors
{
    // Same code and text as the staff equivalent on purpose: one indistinguishable answer for
    // "no such account" and "wrong password".
    public static Error InvalidCredentials() =>
        Error.Validation("auth.invalid_credentials", "Email or password is incorrect.");

    // This one DOES reveal that an address has an account, which is the accepted cost of
    // telling someone to log in instead of silently failing their signup.
    public static Error AlreadyRegistered(string email) =>
        Error.Conflict("customer.already_registered", $"'{email}' already has an account. Log in instead.");

    public static Error NotFound() =>
        Error.NotFound("customer.not_found", "Customer was not found.");
}
