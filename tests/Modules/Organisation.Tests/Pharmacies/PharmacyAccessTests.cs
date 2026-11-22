using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>Who may manage a pharmacy's members: its managers; anyone else learns only what their membership already tells them.</summary>
public class PharmacyAccessTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public void A_stranger_is_told_the_pharmacy_does_not_exist()
    {
        var pharmacy = TestPharmacies.Register(Owner);

        var refused = pharmacy.Require(PersonId.From(Guid.CreateVersion7()), Capability.ManageMembers);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void An_active_employee_lacks_the_capability_to_manage_members()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);

        var refused = pharmacy.Require(Anna, Capability.ManageMembers);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.capability-missing");
        refused.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void A_member_whose_membership_ended_is_told_the_pharmacy_does_not_exist()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Manager);
        pharmacy.EndMembership(anna, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();

        var refused = pharmacy.Require(Anna, Capability.ManageMembers);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void A_manager_is_let_through_with_their_own_membership()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Manager);

        var owner = pharmacy.Require(Owner, Capability.ManageMembers);
        var anna = pharmacy.Require(Anna, Capability.ManageMembers);

        owner.IsSuccess.ShouldBeTrue();
        owner.Value.ShouldBeSameAs(pharmacy.Membership(TestPharmacies.FounderOf(pharmacy)));
        anna.IsSuccess.ShouldBeTrue();
        anna.Value.Person.ShouldBe(Anna);
    }

    [Fact]
    public void A_missing_capability_is_refused_with_its_own_code_and_status()
    {
        PharmacyFailures.CapabilityMissing().Code.ShouldBe("organisation.capability-missing");
        PharmacyFailures.CapabilityMissing().Status.ShouldBe(HttpStatusCode.Forbidden);
    }
}
