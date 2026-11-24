namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>Which capabilities each membership role grants, as Organisation grants them.</summary>
public static class RoleCapabilities
{
    /// <summary>MVP 1: the manager can do everything, an employee nothing administrative.</summary>
    public static bool Grants(this MembershipRole role, Capability capability) =>
        role == MembershipRole.Manager && Enum.IsDefined(capability);
}
