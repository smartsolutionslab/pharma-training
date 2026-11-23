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
    /// The caller's membership that grants the capability. There is normally one; two mean an ending is still on its way
    /// or was dead-lettered, and Compliance cannot tell which one ended. It fails closed: the capability holds only when
    /// every one of them grants it, and then one of them is returned.
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

        if (!memberships.All(membership => membership.Grants(capability)))
        {
            return Failure(AssignmentFailures.CapabilityMissing());
        }

        return Success(memberships[0]);
    }

    /// <summary>That the person has a membership in the pharmacy that has not ended, whatever its role; else not found (docs/adr/0003).</summary>
    public static async Task<Result<Unit, ApiError>> RequireMembershipAsync(IMemberStore members, PersonId person, PharmacyId pharmacy, CancellationToken cancellationToken)
    {
        Ensure.That(members).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();

        var memberships = await members.ActiveMembershipsAsync(person, pharmacy, cancellationToken);
        if (memberships.Count == 0)
        {
            return Failure(AssignmentFailures.PharmacyNotFound(pharmacy.Value));
        }

        return Success(Unit.Value);
    }
}
