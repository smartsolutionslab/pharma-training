using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// Who may do what in a pharmacy, decided by their own membership there, not by a realm role. Someone without an
/// active membership learns nothing about the pharmacy; a member whose role lacks the capability is told so.
/// </summary>
public static class PharmacyAccess
{
    /// <summary>The caller's own active membership, when its role grants the capability.</summary>
    public static Result<Membership, ApiError> Require(this Pharmacy pharmacy, PersonId person, Capability capability)
    {
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(person).IsNotNull();

        var membership = pharmacy.ActiveMembershipOf(person);
        if (!membership.HasValue)
        {
            return Failure(PharmacyFailures.NotFound(pharmacy.Id.Value));
        }

        if (!membership.Value.Role.Grants(capability))
        {
            return Failure(PharmacyFailures.CapabilityMissing());
        }

        return Success(membership.Value);
    }
}
