using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Why an editor returned a version: 1 to 1000 characters after trimming, plain text that may hold line breaks and tabs
/// but no other control or format character, and markup kept as it was written (#181, D3).
/// </summary>
public class ReturnReasonTests
{
    private const string Message =
        "A return's reason is 1 to 1000 characters after trimming, and holds no control or format characters but line breaks and tabs.";

    public static TheoryData<string?> Candidates
    {
        get
        {
            var data = new TheoryData<string?>
            {
                (string?)null,
                string.Empty,
                "   ",
                "\n\t",
                "ok",
                "  Kapitel 2 fehlt.\n",
                new string('a', 1000),
                "  " + new string('a', 1000) + "\n",
                new string('a', 1001),
                "Erstens\r\nZweitens\tDrittens",
                "<b>Kapitel 2</b> & <script>x</script>",
            };
            foreach (var refused in RefusedCharacters)
            {
                data.Add(refused);
            }

            return data;
        }
    }

    /// <summary>A nul, a bell, RLO, a zero-width space (alone too), a line and a paragraph separator, a private-use and an unassigned code point.</summary>
    private static string[] RefusedCharacters =>
        ["a\0b", "a\u0007b", "a\u202Eb", "a\u200Bb", "\u200B\u200B", "a\u2028b", "a\u2029b", "a\uE000b", "a\u0378b"];

    [Fact]
    public void A_reason_is_kept_trimmed() =>
        ReturnReason.From("  Kapitel 2 fehlt.\n").Value.ShouldBe("Kapitel 2 fehlt.");

    [Fact]
    public void A_reason_of_the_maximum_length_is_accepted_even_with_whitespace_around_it()
    {
        ReturnReason.MaxLength.ShouldBe(1000);
        ReturnReason.From(new string('a', 1000)).Value.Length.ShouldBe(1000);
        ReturnReason.From("  " + new string('a', 1000) + "\n").Value.Length.ShouldBe(1000);
    }

    [Fact]
    public void A_reason_longer_than_the_maximum_is_refused()
    {
        ReturnReason.TryFrom(new string('a', 1001), out _).ShouldBeFalse();
        Should.Throw<GuardException>(() => ReturnReason.From(new string('a', 1001)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void A_blank_reason_is_refused(string? value)
    {
        ReturnReason.TryFrom(value, out _).ShouldBeFalse();
        if (value is not null)
        {
            Should.Throw<GuardException>(() => ReturnReason.From(value));
        }
    }

    [Theory]
    [InlineData("a\0b")]
    [InlineData("a\u0007b")]
    [InlineData("a\u202Eb")]
    [InlineData("a\u200Bb")]
    [InlineData("\u200B\u200B")]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    [InlineData("a\uE000b")]
    [InlineData("a\u0378b")]
    public void A_reason_with_a_control_or_format_character_is_refused(string value)
    {
        ReturnReason.TryFrom(value, out _).ShouldBeFalse();
        Should.Throw<GuardException>(() => ReturnReason.From(value)).Message.ShouldContain(Message);
    }

    /// <summary>
    /// An unpaired surrogate cannot be shown either (#181, O3): alone, a high or low one in the middle of text, and a high
    /// one at its end. The value is built from the code unit, since an attribute's string would not keep it unpaired.
    /// </summary>
    [Theory]
    [InlineData("", 0xD800, "")]
    [InlineData("a", 0xD800, "b")]
    [InlineData("a", 0xDC00, "b")]
    [InlineData("Quelle fehlt", 0xDBFF, "")]
    public void A_reason_with_an_unpaired_surrogate_is_refused(string before, int surrogate, string after)
    {
        var value = before + (char)surrogate + after;
        char.IsSurrogate(value[before.Length]).ShouldBeTrue();

        ReturnReason.TryFrom(value, out _).ShouldBeFalse($"TryFrom accepted U+{surrogate:X4} after \"{before}\"");
        Should.Throw<GuardException>(() => ReturnReason.From(value)).Message.ShouldContain(Message);
    }

    [Fact]
    public void Line_breaks_and_tabs_are_kept_inside_a_reason() =>
        ReturnReason.From("Erstens\r\nZweitens\tDrittens").Value.ShouldBe("Erstens\r\nZweitens\tDrittens");

    [Fact]
    public void Markup_is_kept_as_text() =>
        ReturnReason.From("<b>Kapitel 2</b> & <script>x</script>").Value.ShouldBe("<b>Kapitel 2</b> & <script>x</script>");

    [Theory]
    [MemberData(nameof(Candidates))]
    public void TryFrom_succeeds_exactly_when_From_does(string? value)
    {
        var accepted = FromAccepts(value);

        ReturnReason.TryFrom(value, out var reason).ShouldBe(accepted, $"TryFrom and From disagree on \"{value}\"");
        if (accepted)
        {
            reason.ShouldNotBeNull().ShouldBe(ReturnReason.From(value!));
        }
    }

    private static bool FromAccepts(string? value)
    {
        try
        {
            _ = ReturnReason.From(value!);
            return true;
        }
        catch (GuardException)
        {
            return false;
        }
    }
}
