using LeWiK.Store.App.Common.Results;

namespace LeWiK.Store.App.Platform;

public static class PlatformErrors
{
    public static Error StoreNotFound(Guid id) =>
        Error.NotFound("platform.store_not_found", $"Store {id} was not found.");
    public static Error SlugTaken(string slug) =>
        Error.Conflict("platform.slug_taken", $"The slug '{slug}' is already in use.");
    public static Error DomainTaken(string domain) =>
        Error.Conflict("platform.domain_taken", $"The domain '{domain}' is already in use.");
    public static Error StaffEmailTaken(string email) =>
        Error.Conflict("platform.staff_email_taken", $"'{email}' is already a user of this store.");
    public static Error StaffNotFound(Guid id) =>
        Error.NotFound("platform.staff_not_found", $"Staff user {id} was not found.");
    public static Error StoreSuspended() =>
        Error.Conflict("platform.store_suspended", "This store is suspended.");
}
