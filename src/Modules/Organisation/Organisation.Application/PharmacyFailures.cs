namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>Everything the pharmacies' use cases refuse, each with its own code.</summary>
public static class PharmacyFailures
{
    public static ApiError InvalidName() => new InvalidPharmacyName();

    public static ApiError InvalidJobFunction() => new InvalidJobFunction();

    public static ApiError IdentityIncomplete() => new IdentityIncomplete();

    public static ApiError NotFound(Guid id) => new PharmacyNotFound(id);
}
