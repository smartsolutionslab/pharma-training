using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The display name of the editor who approved a question, as the access token's <c>name</c> claim carried it at
/// approval time (#205): trimmed, 1 to 100 characters, and nothing a reader cannot see or that turns the text around:
/// no control, format, private-use or unassigned character and no unpaired surrogate, except the zero-width joiners a
/// script writes names with. The name is the user's own (Keycloak
/// lets them edit it), so it is checked like any input.
/// </summary>
public class ReviewerNameTests
{
    /// <summary>
    /// Names that would show nothing, or end in something that shows nothing (#213's review): Hangul fillers (letters that
    /// draw nothing) and the combining grapheme joiner, which are Default_Ignorable; a variation selector or Mongolian free
    /// variation selector that selects no letter (alone, first, after a space, a digit, a joiner or another selector); a
    /// name with no letter at all (the blank braille pattern); and a joiner ending a word after a mark that is no virama,
    /// or after an invisible one.
    /// </summary>
    private static readonly string[] Invisible =
    [
        "\u3164", "\u115F\u1160", "\uFFA0", "\u3164\u200D\u3164", "\u034F", "Emma\u034FEditor", "\uFE0F", "\u180B",
        "\U000E0100", "\U000E0100\u8FBB", "Emma \uFE0F", "Emma\uFE0F\uFE0F", "Emma 1\uFE0F", "\u8FBB\u200D\U000E0100",
        "\u1820\u180B\u180C", "\u2800", "\u2800 \u2800", "Emma\u0301\u200D", "\u034F\u200D", "\uFE0F\u200C",
        "Emma \u034F\u200C Reza", "a\u200C\u034F\u200Cb",
    ];

    public static TheoryData<string> Refused => [string.Empty, "   ", new string('a', 101), "a\0b", "\u200B", "Emma\u202EEditor", "Emma\u2066Editor", "Emma\nEditor", "Emma\tEditor", "Emma\u0001Editor", "Emma\uD800Editor", "Emma\uE000Editor", "\u200D", "\u200C\u200D", "\u200CEmma", "Emma\u200D", "Emma \u200C Reza", "a\u200C\u200Cb", .. Invisible];

    public static TheoryData<string?> NoName => [(string?)null, string.Empty, "  ", new string('a', 101), "a\0b", "\u200B", "Emma\u202EEditor", "Emma\u2066Editor", "Emma\nEditor", "Emma\tEditor", "Emma\u0001Editor", "Emma\uD800Editor", "Emma\uE000Editor", "\u200D", "\u200C\u200D", "\u200CEmma", "Emma\u200D", "Emma \u200C Reza", "a\u200C\u200Cb", .. Invisible];

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
    /// so they are kept after a letter or mark, and at the end of a word after a virama (Malayalam chillu, Bengali khanda-ta),
    /// while the bidi controls around them stay refused.
    /// </summary>
    [Theory]
    [InlineData("Mohammad\u200CReza")]
    [InlineData("\u0928\u093F\u0930\u094D\u200D\u092E\u0932\u093E")]
    [InlineData("\u0D05\u0D30\u0D4D\u200D\u0D1C\u0D41\u0D28\u0D4D\u200D")]
    [InlineData("\u0D05\u0D30\u0D4D\u200D \u0D1C\u0D41\u0D28\u0D4D")]
    [InlineData("\u09A4\u09CD\u200D \u09B8\u09C7\u09A8")]
    public void A_name_joined_with_zero_width_non_joiner_or_joiner_is_kept(string value)
    {
        ReviewerName.From(value).Value.ShouldBe(value);
        ReviewerName.TryFrom(value, out var name).ShouldBeTrue();
        name.Value.ShouldBe(value);
    }

    /// <summary>
    /// A joiner may end a word after any mark of canonical combining class 9, the viramas (#401's review), not only those
    /// of the Indic scripts: Thai, Lao, Tibetan, Tagalog, Sundanese, Javanese, Meetei Mayek, and the Brahmic scripts outside
    /// the Basic Multilingual Plane, Brahmi, Kaithi and Tulu-Tigalari.
    /// </summary>
    [Theory]
    [InlineData("\u0E01\u0E3A\u200D")]
    [InlineData("\u0E81\u0EBA\u200D")]
    [InlineData("\u0F40\u0F84\u200D")]
    [InlineData("\u1703\u1714\u200D")]
    [InlineData("\u1B8A\u1BAA\u200D")]
    [InlineData("\u1B8A\u1BAB\u200C \u1B8A")]
    [InlineData("\uA98F\uA9C0\u200D")]
    [InlineData("\uAAE0\uAAF6\u200D")]
    [InlineData("\U00011013\U00011046\u200D")]
    [InlineData("\U0001108D\U000110B9\u200D")]
    [InlineData("\U00011380\U000113CE\u200D")]
    public void A_joiner_ending_a_word_after_any_virama_is_kept(string value)
    {
        ReviewerName.From(value).Value.ShouldBe(value);
        ReviewerName.TryFrom(value, out var name).ShouldBeTrue();
        name.Value.ShouldBe(value);
    }

    /// <summary>
    /// A variation selector right after a letter, or after a letter and its marks, picks how that letter is drawn, so real
    /// names hold one (#401's review): a Japanese name with an ideographic variation sequence (辻 with U+E0100), a
    /// Mongolian name with a free variation selector (FVS1 to FVS4), and a standardized variant (U+FE00 to U+FE0F).
    /// </summary>
    [Theory]
    [InlineData("\u8FBB\U000E0100\u592A\u90CE")]
    [InlineData("\u8FBB\U000E0100 \u592A\u90CE")]
    [InlineData("\u1823\u1834\u180B\u1822\u1828")]
    [InlineData("\u182A\u1820\u1832\u180C\u1820\u1828")]
    [InlineData("\u1828\u1820\u180D\u1833\u1820")]
    [InlineData("\u1832\u180F\u1823\u1837\u1820")]
    [InlineData("Emma\u180DEditor")]
    [InlineData("Emma\uFE0F")]
    [InlineData("\u1000\uFE00\u1001")]
    [InlineData("Zoe\u0308\uFE0E Editor")]
    public void A_name_with_a_variation_selector_after_a_letter_is_kept(string value)
    {
        ReviewerName.From(value).Value.ShouldBe(value);
        ReviewerName.TryFrom(value, out var name).ShouldBeTrue();
        name.Value.ShouldBe(value);
    }

    /// <summary>A combining mark that shows (a diaeresis written apart from its letter) keeps a name a name.</summary>
    [Fact]
    public void A_name_with_a_visible_combining_mark_is_kept() =>
        ReviewerName.From("Zoe\u0308 Editor").Value.ShouldBe("Zoe\u0308 Editor");

    [Fact]
    public void TryFrom_trims_a_reviewer_name_it_accepts()
    {
        ReviewerName.TryFrom("\tEmma Editor  ", out var name).ShouldBeTrue();
        name.Value.ShouldBe("Emma Editor");
        name.ToString().ShouldBe("Emma Editor");
    }
}
