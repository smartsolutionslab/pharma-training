using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The display name of the editor who approved a question, as the access token's <c>name</c> claim carried it at
/// approval time (#205): trimmed, 1 to 100 characters, no nul character.
/// </summary>
public class ReviewerNameTests
{
    public static TheoryData<string> Refused => [string.Empty, "   ", new string('a', 101), "a\0b"];

    public static TheoryData<string?> NoName => [(string?)null, string.Empty, "  ", new string('a', 101), "a\0b"];

    [Fact]
    public void A_reviewer_name_is_kept_trimmed() =>
        ReviewerName.From("  Emma Editor ").Value.ShouldBe("Emma Editor");

    [Fact]
    public void A_reviewer_name_of_the_maximum_length_is_kept()
    {
        ReviewerName.MaxLength.ShouldBe(100);
        ReviewerName.From(new string('a', 100)).Value.ShouldBe(new string('a', 100));
        ReviewerName.From(" " + new string('a', 100) + " ").Value.Length.ShouldBe(100);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_blank_or_too_long_reviewer_name_or_one_holding_a_nul_character_is_refused(string value) =>
        Should.Throw<GuardException>(() => ReviewerName.From(value));

    [Theory]
    [MemberData(nameof(NoName))]
    public void TryFrom_finds_no_reviewer_name_in_a_missing_blank_too_long_or_nul_value(string? value)
    {
        ReviewerName.TryFrom(value, out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

    [Fact]
    public void TryFrom_trims_a_reviewer_name_it_accepts()
    {
        ReviewerName.TryFrom("\tEmma Editor  ", out var name).ShouldBeTrue();
        name.Value.ShouldBe("Emma Editor");
        name.ToString().ShouldBe("Emma Editor");
    }
}
