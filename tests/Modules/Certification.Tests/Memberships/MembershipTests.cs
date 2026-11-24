using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>
/// The local copies of Organisation's memberships: their identities, the factories that refuse a missing part, and the
/// current role, the start's until a change with a higher version replaces it (docs/adr/0015).
/// </summary>
public class MembershipTests
{
    private static readonly MembershipId Id = MembershipId.From(Guid.CreateVersion7());
    private static readonly PersonId Person = PersonId.From(Guid.CreateVersion7());
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly HolderName Name = HolderName.From("Anna Berger");
    private static readonly Timestamp At = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void An_empty_guid_is_no_membership_id_and_no_pharmacy_id()
    {
        Should.Throw<GuardException>(() => MembershipId.From(Guid.Empty));
        Should.Throw<GuardException>(() => PharmacyId.From(Guid.Empty));
    }

    [Fact]
    public void A_membership_id_and_a_pharmacy_id_come_back_from_their_value()
    {
        var value = Guid.CreateVersion7();

        MembershipId.From(value).Value.ShouldBe(value);
        MembershipId.From(value).ToString().ShouldBe(value.ToString());
        PharmacyId.From(value).Value.ShouldBe(value);
        PharmacyId.From(value).ToString().ShouldBe(value.ToString());
    }

    [Fact]
    public void A_started_membership_keeps_what_it_was_given()
    {
        var membership = Membership.Started(Id, Person, Pharmacy, Name, MembershipRole.Manager, At);

        membership.Id.ShouldBe(Id);
        membership.Person.ShouldBe(Person);
        membership.Pharmacy.ShouldBe(Pharmacy);
        membership.HolderName.ShouldBe(Name);
        membership.StartedAt.ShouldBe(At);
        membership.Role.ShouldBe(MembershipRole.Manager);
    }

    [Theory]
    [InlineData(MembershipRole.Manager)]
    [InlineData(MembershipRole.Employee)]
    public void A_started_membership_keeps_its_role(MembershipRole role)
    {
        Membership.Started(Id, Person, Pharmacy, Name, role, At).Role.ShouldBe(role);
    }

    [Theory]
    [InlineData(MembershipRole.Manager)]
    [InlineData(MembershipRole.Employee)]
    public void Without_changes_the_current_role_is_the_one_it_started_with(MembershipRole role)
    {
        var membership = Membership.Started(Id, Person, Pharmacy, Name, role, At);

        membership.CurrentRole([]).ShouldBe(role);
    }

    [Fact]
    public void The_change_with_the_highest_version_gives_the_current_role_whatever_order_the_changes_come_in()
    {
        var membership = Membership.Started(Id, Person, Pharmacy, Name, MembershipRole.Employee, At);
        var promoted = ChangeOf(Id, 3, MembershipRole.Manager);
        var demoted = ChangeOf(Id, 2, MembershipRole.Employee);

        membership.CurrentRole([promoted, demoted]).ShouldBe(MembershipRole.Manager);
        membership.CurrentRole([demoted, promoted]).ShouldBe(MembershipRole.Manager);
    }

    [Fact]
    public void A_later_demotion_takes_the_managers_role_away()
    {
        var membership = Membership.Started(Id, Person, Pharmacy, Name, MembershipRole.Manager, At);

        membership.CurrentRole([ChangeOf(Id, 2, MembershipRole.Employee)]).ShouldBe(MembershipRole.Employee);
    }

    [Fact]
    public void A_change_of_another_membership_is_refused()
    {
        var membership = Membership.Started(Id, Person, Pharmacy, Name, MembershipRole.Employee, At);

        Should.Throw<GuardException>(() => membership.CurrentRole([ChangeOf(MembershipId.From(Guid.CreateVersion7()), 2, MembershipRole.Manager)]));
    }

    [Fact]
    public void A_started_membership_without_any_of_its_parts_is_refused()
    {
        Should.Throw<GuardException>(() => Membership.Started(null!, Person, Pharmacy, Name, MembershipRole.Manager, At));
        Should.Throw<GuardException>(() => Membership.Started(Id, null!, Pharmacy, Name, MembershipRole.Manager, At));
        Should.Throw<GuardException>(() => Membership.Started(Id, Person, null!, Name, MembershipRole.Manager, At));
        Should.Throw<GuardException>(() => Membership.Started(Id, Person, Pharmacy, null!, MembershipRole.Manager, At));
        Should.Throw<GuardException>(() => Membership.Started(Id, Person, Pharmacy, Name, MembershipRole.Manager, null!));
    }

    [Fact]
    public void An_ending_keeps_what_it_was_given()
    {
        var ending = MembershipEnding.Of(Id, Person, Pharmacy, At);

        ending.Id.ShouldBe(Id);
        ending.Person.ShouldBe(Person);
        ending.Pharmacy.ShouldBe(Pharmacy);
        ending.EndedAt.ShouldBe(At);
    }

    [Fact]
    public void An_ending_without_any_of_its_parts_is_refused()
    {
        Should.Throw<GuardException>(() => MembershipEnding.Of(null!, Person, Pharmacy, At));
        Should.Throw<GuardException>(() => MembershipEnding.Of(Id, null!, Pharmacy, At));
        Should.Throw<GuardException>(() => MembershipEnding.Of(Id, Person, null!, At));
        Should.Throw<GuardException>(() => MembershipEnding.Of(Id, Person, Pharmacy, null!));
    }

    private static MembershipChange ChangeOf(MembershipId membership, int version, MembershipRole role) =>
        MembershipChange.Of(membership, Person, Pharmacy, MembershipVersion.From(version), role);
}
