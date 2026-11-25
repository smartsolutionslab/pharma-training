using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

/// <summary>
/// Erasing a person (#115) forgets the name on Compliance's copies of their memberships and keeps everything else: the
/// PersonId stays as the pseudonym. A membership whose name is gone reads as an empty name in its DTO.
/// </summary>
public class MemberErasureTests
{
    private readonly MembershipId membership = MembershipId.From(Guid.CreateVersion7());
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());

    [Fact]
    public void Erasing_a_members_name_keeps_its_person_pharmacy_job_function_and_role()
    {
        var member = Member.Started(membership, person, pharmacy, MemberName.From("Ada Lovelace"), JobFunction.Pka, MembershipRole.Manager);

        member.EraseName();

        member.Name.ShouldBeNull();
        member.Id.ShouldBe(membership);
        member.Person.ShouldBe(person);
        member.Pharmacy.ShouldBe(pharmacy);
        member.JobFunction.ShouldBe(JobFunction.Pka);
        member.Role.ShouldBe(MembershipRole.Manager);
    }

    [Fact]
    public void Erasing_a_members_name_twice_leaves_it_erased()
    {
        var member = Member.Started(membership, person, pharmacy, MemberName.From("Ada Lovelace"), JobFunction.Pta, MembershipRole.Employee);

        member.EraseName();
        member.EraseName();

        member.Name.ShouldBeNull();
    }

    [Fact]
    public void Erasing_a_changes_name_keeps_its_version_job_function_and_role()
    {
        var change = MembershipChange.Of(membership, person, pharmacy, MembershipVersion.From(2), MemberName.From("Ada Byron"), JobFunction.Pharmacist, MembershipRole.Manager);

        change.EraseName();

        change.Name.ShouldBeNull();
        change.Membership.ShouldBe(membership);
        change.Person.ShouldBe(person);
        change.Version.ShouldBe(MembershipVersion.From(2));
        change.JobFunction.ShouldBe(JobFunction.Pharmacist);
        change.Role.ShouldBe(MembershipRole.Manager);
    }

    [Fact]
    public void A_membership_whose_name_is_erased_maps_to_an_empty_name()
    {
        var member = Member.Started(membership, person, pharmacy, MemberName.From("Ada Lovelace"), JobFunction.Pta, MembershipRole.Employee);
        member.EraseName();

        var dto = CurrentMembership.Of(member, []).ToDto();

        dto.Name.ShouldBe(string.Empty);
        dto.PersonId.ShouldBe(person.Value);
    }

    [Fact]
    public void A_membership_whose_latest_change_has_its_name_erased_maps_to_an_empty_name()
    {
        var member = Member.Started(membership, person, pharmacy, MemberName.From("Ada Lovelace"), JobFunction.Pta, MembershipRole.Employee);
        var change = MembershipChange.Of(membership, person, pharmacy, MembershipVersion.From(2), MemberName.From("Ada Byron"), JobFunction.Pta, MembershipRole.Manager);
        member.EraseName();
        change.EraseName();

        CurrentMembership.Of(member, [change]).ToDto().Name.ShouldBe(string.Empty);
    }
}
