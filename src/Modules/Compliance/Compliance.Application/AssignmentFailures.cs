namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>Everything the assignments' use cases refuse, each with its own code.</summary>
public static class AssignmentFailures
{
    public static ApiError PharmacyNotFound(Guid id) => new PharmacyNotFound(id);

    public static ApiError CapabilityMissing() => new CapabilityMissing();
}
