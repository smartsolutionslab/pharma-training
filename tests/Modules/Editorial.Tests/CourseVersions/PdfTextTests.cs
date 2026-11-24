using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The text extracted from a PDF chapter's pages (#157): never blank, at most as long as the longest text chapter, and
/// without the nul character PostgreSQL's <c>text</c> cannot hold.
/// </summary>
public class PdfTextTests
{
    [Fact]
    public void A_pdf_text_is_at_most_as_long_as_a_markdown_text()
    {
        PdfText.MaxLength.ShouldBe(200_000);
        PdfText.MaxLength.ShouldBe(MarkdownText.MaxLength);
    }

    [Theory]
    [InlineData("Zytostatika werden getrennt gelagert.")]
    [InlineData("Seite 1\n\nSeite 2")]
    public void A_pdf_text_keeps_its_value_exactly(string value)
    {
        PdfText.From(value).Value.ShouldBe(value);
    }

    [Fact]
    public void A_pdf_text_of_exactly_the_maximum_length_is_taken()
    {
        var value = new string('a', PdfText.MaxLength);

        PdfText.From(value).Value.Length.ShouldBe(PdfText.MaxLength);
        PdfText.TryFrom(value, out var text).ShouldBeTrue();
        text.ShouldNotBeNull().Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void A_blank_pdf_text_is_refused(string value)
    {
        Should.Throw<GuardException>(() => PdfText.From(value));
        PdfText.TryFrom(value, out var text).ShouldBeFalse();
        text.ShouldBeNull();
    }

    [Fact]
    public void A_pdf_text_above_the_maximum_length_is_refused()
    {
        var value = new string('a', PdfText.MaxLength + 1);

        Should.Throw<GuardException>(() => PdfText.From(value));
        PdfText.TryFrom(value, out var text).ShouldBeFalse();
        text.ShouldBeNull();
    }

    [Fact]
    public void A_pdf_text_with_a_nul_character_is_refused()
    {
        const string Value = "Lagerung\0von Zytostatika";

        Should.Throw<GuardException>(() => PdfText.From(Value));
        PdfText.TryFrom(Value, out var text).ShouldBeFalse();
        text.ShouldBeNull();
    }

    [Fact]
    public void No_pdf_text_is_made_of_null()
    {
        Should.Throw<GuardException>(() => PdfText.From(null!));
        PdfText.TryFrom(null, out var text).ShouldBeFalse();
        text.ShouldBeNull();
    }

    [Fact]
    public void A_pdf_text_reads_as_its_value()
    {
        PdfText.From("Kühlkette").ToString().ShouldBe("Kühlkette");
    }
}
