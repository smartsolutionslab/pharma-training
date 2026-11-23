using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

public class MemberIdentityTests
{
    [Fact]
    public void A_membership_id_comes_back_from_its_value()
    {
        var value = Guid.CreateVersion7();

        var id = MembershipId.From(value);

        id.Value.ShouldBe(value);
        id.ToString().ShouldBe(value.ToString());
        id.ShouldBe(MembershipId.From(value));
    }

    [Fact]
    public void A_person_id_comes_back_from_its_value()
    {
        var value = Guid.CreateVersion7();

        var id = PersonId.From(value);

        id.Value.ShouldBe(value);
        id.ToString().ShouldBe(value.ToString());
        id.ShouldBe(PersonId.From(value));
    }

    [Fact]
    public void A_pharmacy_id_comes_back_from_its_value()
    {
        var value = Guid.CreateVersion7();

        var id = PharmacyId.From(value);

        id.Value.ShouldBe(value);
        id.ToString().ShouldBe(value.ToString());
        id.ShouldBe(PharmacyId.From(value));
    }

    [Fact]
    public void An_empty_guid_is_no_membership_id() =>
        Should.Throw<GuardException>(() => MembershipId.From(Guid.Empty));

    [Fact]
    public void An_empty_guid_is_no_person_id() =>
        Should.Throw<GuardException>(() => PersonId.From(Guid.Empty));

    [Fact]
    public void An_empty_guid_is_no_pharmacy_id() =>
        Should.Throw<GuardException>(() => PharmacyId.From(Guid.Empty));

    [Fact]
    public void A_membership_version_comes_back_from_its_value()
    {
        var version = MembershipVersion.From(3);

        version.Value.ShouldBe(3);
        version.ShouldBe(MembershipVersion.From(3));
        MembershipVersion.From(1).ShouldBe(MembershipVersion.First);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_membership_version_below_one_is_refused(int value) =>
        Should.Throw<GuardException>(() => MembershipVersion.From(value));

    [Fact]
    public void A_higher_membership_version_compares_greater()
    {
        var older = MembershipVersion.From(2);
        var newer = MembershipVersion.From(3);

        (newer > older).ShouldBeTrue();
        (older < newer).ShouldBeTrue();
        (newer >= MembershipVersion.From(3)).ShouldBeTrue();
        (older <= MembershipVersion.From(2)).ShouldBeTrue();
        newer.CompareTo(older).ShouldBeGreaterThan(0);
        older.CompareTo(newer).ShouldBeLessThan(0);
        MembershipVersion.First.Next().ShouldBe(older);
    }
}
