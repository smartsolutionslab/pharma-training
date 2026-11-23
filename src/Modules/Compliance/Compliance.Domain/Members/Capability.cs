namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

/// <summary>
/// What a member's role allows in Compliance, asked of the caller's membership in the pharmacy (docs/adr/0003): assigning
/// courses, and viewing the team's compliance, which a later role may hold without assigning.
/// </summary>
public enum Capability
{
    AssignCourses,
    ViewTeamCompliance,
}
