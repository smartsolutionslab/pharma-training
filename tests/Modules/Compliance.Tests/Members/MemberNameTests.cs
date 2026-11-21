using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

public class MemberNameTests
{
    [Fact]
    public void A_name_holds_its_value()
    {
        var name = MemberName.From("Ada Lovelace");

        name.Value.ShouldBe("Ada Lovelace");
        name.ToString().ShouldBe("Ada Lovelace");
        name.ShouldBe(MemberName.From("Ada Lovelace"));
    }

    [Fact]
    public void A_name_is_kept_as_organisation_sent_it_without_trimming() =>
        MemberName.From(" Ada ").Value.ShouldBe(" Ada ");

    [Fact]
    public void A_name_as_long_as_organisation_allows_is_a_name() =>
        MemberName.From(new string('a', 100)).Value.Length.ShouldBe(100);

    [Fact]
    public void A_name_longer_than_organisation_allows_is_refused() =>
        Should.Throw<GuardException>(() => MemberName.From(new string('a', 101)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ada\0Lovelace")]
    public void A_missing_or_blank_name_or_one_holding_a_nul_character_is_refused(string? value) =>
        Should.Throw<GuardException>(() => MemberName.From(value!));
}
