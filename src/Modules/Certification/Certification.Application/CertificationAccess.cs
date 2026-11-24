using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>
/// Who may do what in a pharmacy, decided by Certification's own copy of the caller's membership there and its current
/// role, not by a realm role: Certification's counterpart of Organisation's PharmacyAccess. Someone without a membership
/// there that has not ended learns nothing about the pharmacy; a member whose current role lacks the capability is told
/// so (docs/adr/0003-per-pharmacy-capabilities-are-checked-in-the-handler.md).
/// </summary>
public static class CertificationAccess
{
    /// <summary>
    /// That the person holds the capability in the pharmacy. There is normally one active membership; two mean an ending
    /// is still on its way or was dead-lettered, and Certification cannot tell which one ended. It fails closed: the
    /// capability holds only when every one of them grants it.
    /// </summary>
    public static async Task<Result<Unit, ApiError>> RequireAsync(
        IMembershipStore memberships,
        PersonId person,
        PharmacyId pharmacy,
        Capability capability,
        CancellationToken cancellationToken)
    {
        Ensure.That(memberships).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();

        var roles = await memberships.ActiveRolesAsync(person, pharmacy, cancellationToken);
        if (roles.Count == 0)
        {
            return Failure(ExportFailures.PharmacyNotFound(pharmacy.Value));
        }

        if (!roles.All(role => role.Grants(capability)))
        {
            return Failure(ExportFailures.CapabilityMissing());
        }

        return Success(Unit.Value);
    }
}
