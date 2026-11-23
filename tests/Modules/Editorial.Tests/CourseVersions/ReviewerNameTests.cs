using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The display name of the editor who approved a question, as the access token's <c>name</c> claim carried it at
/// approval time (#205): trimmed, 1 to 100 characters, and nothing a reader cannot see or that turns the text around:
/// no control, format, private-use or unassigned character and no unpaired surrogate. The name is the user's own (Keycloak
/// lets them edit it), so it is checked like any input.
/// </summary>
public class ReviewerNameTests
{
    public static TheoryData<string> Refused => [string.Empty, "   ", new string('a', 101), "a\0b", "\u200B", "Emma\u202EEditor", "Emma\u2066Editor", "Emma\nEditor", "Emma\tEditor", "Emma\u0001Editor", "Emma\uD800Editor", "Emma\uE000Editor", "\u200D", "\u200C\u200D", "\u200CEmma", "Emma\u200D", "Emma \u200C Reza"];

    public static TheoryData<string?> NoName => [(string?)null, string.Empty, "  ", new string('a', 101), "a\0b", "\u200B", "Emma\u202EEditor", "Emma\u2066Editor", "Emma\nEditor", "Emma\tEditor", "Emma\u0001Editor", "Emma\uD800Editor", "Emma\uE000Editor", "\u200D", "\u200C\u200D", "\u200CEmma", "Emma\u200D", "Emma \u200C Reza"];

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
    public void A_blank_or_too_long_reviewer_name_or_one_holding_an_unseen_or_reversing_character_is_refused(string value) =>
        Should.Throw<GuardException>(() => ReviewerName.From(value));

    [Theory]
    [MemberData(nameof(NoName))]
    public void TryFrom_finds_no_reviewer_name_in_a_missing_blank_too_long_or_unseen_or_reversing_value(string? value)
    {
        ReviewerName.TryFrom(value, out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

    /// <summary>
    /// The zero-width non-joiner and joiner are part of how Persian and Indic names are written ("MohammadReza", "Indu"),
    /// so they are kept, while the bidi controls around them stay refused.
    /// </summary>
    [Theory]
    [InlineData("Mohammad\u200CReza")]
    [InlineData("\u0928\u093F\u0930\u094D\u200D\u092E\u0932\u093E")]
    public void A_name_joined_with_zero_width_non_joiner_or_joiner_is_kept(string value)
    {
        ReviewerName.From(value).Value.ShouldBe(value);
        ReviewerName.TryFrom(value, out var name).ShouldBeTrue();
        name.Value.ShouldBe(value);
    }

    [Fact]
    public void TryFrom_trims_a_reviewer_name_it_accepts()
    {
        ReviewerName.TryFrom("\tEmma Editor  ", out var name).ShouldBeTrue();
        name.Value.ShouldBe("Emma Editor");
        name.ToString().ShouldBe("Emma Editor");
    }
}
