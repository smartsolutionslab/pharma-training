namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Where a membership stands, from the invitation to the day the member leaves.</summary>
public enum MembershipStatus
{
    Invited,
    Active,
    Ended,
    Revoked,
}
