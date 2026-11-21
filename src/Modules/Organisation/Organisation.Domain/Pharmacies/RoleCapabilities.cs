namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Which capabilities each membership role grants.</summary>
public static class RoleCapabilities
{
    /// <summary>MVP 1: the manager can do everything, an employee nothing administrative.</summary>
    public static bool Grants(this MembershipRole role, Capability capability) =>
        role == MembershipRole.Manager && Enum.IsDefined(capability);
}
