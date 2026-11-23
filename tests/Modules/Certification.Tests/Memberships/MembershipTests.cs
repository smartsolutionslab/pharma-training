using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>The local copies of Organisation's memberships: their identities and the factories that refuse a missing part.</summary>
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
        var membership = Membership.Started(Id, Person, Pharmacy, Name, At);

        membership.Id.ShouldBe(Id);
        membership.Person.ShouldBe(Person);
        membership.Pharmacy.ShouldBe(Pharmacy);
        membership.HolderName.ShouldBe(Name);
        membership.StartedAt.ShouldBe(At);
    }

    [Fact]
    public void A_started_membership_without_any_of_its_parts_is_refused()
    {
        Should.Throw<GuardException>(() => Membership.Started(null!, Person, Pharmacy, Name, At));
        Should.Throw<GuardException>(() => Membership.Started(Id, null!, Pharmacy, Name, At));
        Should.Throw<GuardException>(() => Membership.Started(Id, Person, null!, Name, At));
        Should.Throw<GuardException>(() => Membership.Started(Id, Person, Pharmacy, null!, At));
        Should.Throw<GuardException>(() => Membership.Started(Id, Person, Pharmacy, Name, null!));
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
}
