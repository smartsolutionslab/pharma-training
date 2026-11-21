namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>What a role allows. Endpoints ask for a capability, never for a role, so new roles need no endpoint change.</summary>
public enum Capability
{
    ManageMembers,
    AssignCourses,
}
