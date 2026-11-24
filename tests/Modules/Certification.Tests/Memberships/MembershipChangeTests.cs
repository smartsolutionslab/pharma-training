using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>
/// Certification's copy of a change Organisation made to a membership: the membership, the version and the role, and
/// nothing else (no name, no job function), at a version after the start's.
/// </summary>
public class MembershipChangeTests
{
    private static readonly MembershipId Id = MembershipId.From(Guid.CreateVersion7());
    private static readonly PersonId Person = PersonId.From(Guid.CreateVersion7());
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());

    [Fact]
    public void A_change_keeps_what_it_was_given()
    {
        var change = MembershipChange.Of(Id, Person, Pharmacy, MembershipVersion.From(2), MembershipRole.Manager);

        change.Membership.ShouldBe(Id);
        change.Person.ShouldBe(Person);
        change.Pharmacy.ShouldBe(Pharmacy);
        change.Version.ShouldBe(MembershipVersion.From(2));
        change.Role.ShouldBe(MembershipRole.Manager);
    }

    [Fact]
    public void A_change_at_version_one_is_refused()
    {
        Should.Throw<GuardException>(() => MembershipChange.Of(Id, Person, Pharmacy, MembershipVersion.First, MembershipRole.Manager));
    }

    [Fact]
    public void A_change_without_any_of_its_parts_is_refused()
    {
        var version = MembershipVersion.From(2);

        Should.Throw<GuardException>(() => MembershipChange.Of(null!, Person, Pharmacy, version, MembershipRole.Manager));
        Should.Throw<GuardException>(() => MembershipChange.Of(Id, null!, Pharmacy, version, MembershipRole.Manager));
        Should.Throw<GuardException>(() => MembershipChange.Of(Id, Person, null!, version, MembershipRole.Manager));
        Should.Throw<GuardException>(() => MembershipChange.Of(Id, Person, Pharmacy, null!, MembershipRole.Manager));
    }

    [Fact]
    public void The_first_version_is_one_and_none_below_it_exists()
    {
        MembershipVersion.First.Value.ShouldBe(1);
        MembershipVersion.From(1).ShouldBe(MembershipVersion.First);
        Should.Throw<GuardException>(() => MembershipVersion.From(0));
    }

    [Fact]
    public void A_higher_version_is_a_newer_state()
    {
        (MembershipVersion.From(3) > MembershipVersion.From(2)).ShouldBeTrue();
        (MembershipVersion.From(2) < MembershipVersion.From(3)).ShouldBeTrue();
        (MembershipVersion.From(2) > MembershipVersion.From(2)).ShouldBeFalse();
        MembershipVersion.From(2).CompareTo(MembershipVersion.From(10)).ShouldBeLessThan(0);
    }
}
