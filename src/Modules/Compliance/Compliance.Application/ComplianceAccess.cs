using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// Who may do what in a pharmacy, decided by Compliance's own copy of the caller's membership there and its current
/// role, not by a realm role: Compliance's counterpart of Organisation's PharmacyAccess. Someone without a membership
/// there that has not ended learns nothing about the pharmacy; a member whose current role lacks the capability is told
/// so (docs/adr/0003-per-pharmacy-capabilities-are-checked-in-the-handler.md).
/// </summary>
public static class ComplianceAccess
{
    /// <summary>
    /// The caller's membership that grants the capability. There is normally one; two exist only while an ending is
    /// still on its way, and either granting suffices.
    /// </summary>
    public static async Task<Result<CurrentMembership, ApiError>> RequireAsync(
        IMemberStore members,
        PersonId person,
        PharmacyId pharmacy,
        Capability capability,
        CancellationToken cancellationToken)
    {
        Ensure.That(members).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();

        var memberships = await members.ActiveMembershipsAsync(person, pharmacy, cancellationToken);
        if (memberships.Count == 0)
        {
            return Failure(AssignmentFailures.PharmacyNotFound(pharmacy.Value));
        }

        var granting = memberships.FirstOrDefault(membership => membership.Grants(capability));
        if (granting is null)
        {
            return Failure(AssignmentFailures.CapabilityMissing());
        }

        return Success(granting);
    }
}
