using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>An active membership maps to a member, an invited one to an invitation; any other pairing is a bug.</summary>
public class MembershipMappingsTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public void An_active_membership_maps_to_a_member()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Manager, JobFunction.Trainee, "Anna Berger");

        var member = pharmacy.Membership(anna).ToPharmacyMemberDto();

        member.ShouldBe(new PharmacyMemberDto(
            anna.Value,
            Anna.Value,
            "Anna Berger",
            OrganisationJobFunction.Trainee,
            OrganisationMembershipRole.Manager,
            TestPharmacies.RegisteredAt.Value));
    }

    [Fact]
    public void An_invited_membership_maps_to_an_invitation()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var at = TestPharmacies.RegisteredAt.PlusDays(2);
        var invited = TestPharmacies.Invite(pharmacy, "invitee@example.org", at);

        var invitation = pharmacy.Membership(invited).ToPharmacyInvitationDto();

        invitation.ShouldBe(new PharmacyInvitationDto(
            invited.Value,
            "invitee@example.org",
            OrganisationJobFunction.Pta,
            OrganisationMembershipRole.Employee,
            at.PlusDays(14).Value));
    }

    [Fact]
    public void Mapping_an_invited_membership_as_a_member_is_a_bug()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var invited = pharmacy.Membership(TestPharmacies.Invite(pharmacy, "invitee@example.org"));

        Should.Throw<UnreachableException>(() => invited.ToPharmacyMemberDto());
    }

    [Fact]
    public void Mapping_an_active_membership_as_an_invitation_is_a_bug()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var founder = pharmacy.Membership(TestPharmacies.FounderOf(pharmacy));

        Should.Throw<UnreachableException>(() => founder.ToPharmacyInvitationDto());
    }
}
