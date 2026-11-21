using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

public static class PharmacyMappings
{
    /// <summary>The pharmacy as the member of <paramref name="membership"/> sees it.</summary>
    public static MyPharmacyDto ToMyPharmacyDto(this Pharmacy pharmacy, Membership membership)
    {
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(membership).IsNotNull();

        return new MyPharmacyDto(
            pharmacy.Id.Value,
            pharmacy.Name.Value,
            membership.Id.Value,
            membership.Role.ToDto(),
            membership.JobFunction.ToDto(),
            membership.Role.Grants(Capability.ManageMembers),
            membership.Role.Grants(Capability.AssignCourses));
    }
}
