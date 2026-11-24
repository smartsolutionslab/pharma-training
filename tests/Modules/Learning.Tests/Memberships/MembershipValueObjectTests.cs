using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Memberships;

/// <summary>The keys of Learning's copy of the memberships, copied from Compliance's (#194).</summary>
public class MembershipValueObjectTests
{
    [Fact]
    public void An_empty_guid_is_no_membership_id() =>
        Should.Throw<GuardException>(() => MembershipId.From(Guid.Empty));

    [Fact]
    public void A_membership_id_keeps_its_guid()
    {
        var value = Guid.CreateVersion7();

        var id = MembershipId.From(value);

        id.Value.ShouldBe(value);
        id.ToString().ShouldBe(value.ToString());
        id.ShouldBe(MembershipId.From(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_membership_version_below_one_is_refused(int value) =>
        Should.Throw<GuardException>(() => MembershipVersion.From(value));

    [Fact]
    public void A_membership_version_keeps_its_number()
    {
        var version = MembershipVersion.From(3);

        version.Value.ShouldBe(3);
        version.ToString().ShouldBe("3");
        version.ShouldBe(MembershipVersion.From(3));
    }
}
