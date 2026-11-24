using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.PdfExtraction;

/// <summary>
/// What PdfPig reads from a PDF chapter (#157): the pages' text in page order, normalised, cut at the module's limits,
/// and "no text" for a scan, a file that is no readable PDF or one locked with a password, never an exception. The
/// document's metadata is never read. The PDFs are built in the test (<see cref="TestPdfs"/>).
/// </summary>
public sealed class PdfPigTextExtractorTests
{
    private const string Sentence = "Zytostatika werden getrennt von anderen Arzneimitteln gelagert.";

    private readonly FakeLogger<PdfPigTextExtractor> logger = new();

    [Fact]
    public void The_extractor_reads_at_most_500_pages_and_needs_50_letters()
    {
        PdfPigTextExtractor.MaxPages.ShouldBe(500);
        PdfPigTextExtractor.MinimumLetters.ShouldBe(50);
    }

    [Fact]
    public void A_pdf_with_text_on_two_pages_is_complete_with_the_pages_text_in_order_joined_by_a_blank_line()
    {
        var pdf = TestPdfs.Pages("Seite eins: Zytostatika werden getrennt gelagert.\nZweite Zeile ueber Kuehlung.", "Seite zwei: Handschuhe tragen.");

        var extracted = Extract(pdf);

        extracted.Extraction.ShouldBe(PdfTextExtraction.Complete);
        extracted.Text.ShouldNotBeNull().Value.ShouldBe("Seite eins: Zytostatika werden getrennt gelagert.\nZweite Zeile ueber Kuehlung.\n\nSeite zwei: Handschuhe tragen.");
        logger.Collector.Count.ShouldBe(0);
    }

    [Fact]
    public void A_page_that_is_only_a_picture_has_no_text()
    {
        var extracted = Extract(TestPdfs.ImageOnly());

        extracted.Extraction.ShouldBe(PdfTextExtraction.NoText);
        extracted.Text.ShouldBeNull();
    }

    [Fact]
    public void Empty_pages_have_no_text()
    {
        var extracted = Extract(TestPdfs.EmptyPages(3));

        extracted.Extraction.ShouldBe(PdfTextExtraction.NoText);
        extracted.Text.ShouldBeNull();
    }

    [Theory]
    [InlineData(49, PdfTextExtraction.NoText)]
    [InlineData(50, PdfTextExtraction.Complete)]
    public void Text_with_fewer_than_50_letters_is_no_text(int letters, PdfTextExtraction expected)
    {
        // Digits, spaces and punctuation are no letters: a page number or a stamp is not a chapter's text.
        var pdf = TestPdfs.Pages($"{new string('A', letters)} 12345 - 67 / 89.", "- 2 -");

        var extracted = Extract(pdf);

        extracted.Extraction.ShouldBe(expected);
        (extracted.Text is not null).ShouldBe(expected == PdfTextExtraction.Complete);
    }

    [Fact]
    public void Pages_that_only_carry_page_numbers_have_no_text()
    {
        var extracted = Extract(TestPdfs.Pages("Seite 1 von 3", "Seite 2 von 3", "Seite 3 von 3"));

        extracted.Extraction.ShouldBe(PdfTextExtraction.NoText);
        extracted.Text.ShouldBeNull();
    }

    [Fact]
    public void The_stub_the_integration_tests_upload_has_no_text_and_does_not_throw()
    {
        var extracted = Extract(TestPdfs.Stub);

        extracted.Extraction.ShouldBe(PdfTextExtraction.NoText);
        extracted.Text.ShouldBeNull();
    }

    [Fact]
    public void Noise_after_a_pdf_header_has_no_text_does_not_throw_and_logs_one_warning_without_its_message()
    {
        var extracted = Extract(TestPdfs.HeaderThenNoise(4_096));

        extracted.Extraction.ShouldBe(PdfTextExtraction.NoText);
        extracted.Text.ShouldBeNull();
        var warning = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Exception.ShouldBeNull("an exception's message may quote the file; only its type is logged");
        warning.Message.ShouldNotContain("xref", Case.Insensitive);
    }

    [Fact]
    public void A_pdf_locked_with_a_user_password_has_no_text_and_logs_one_warning_naming_the_exceptions_type_only()
    {
        var extracted = Extract(TestPdfs.EncryptedWithAUserPassword);

        extracted.Extraction.ShouldBe(PdfTextExtraction.NoText);
        extracted.Text.ShouldBeNull();
        var warning = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Exception.ShouldBeNull();
        warning.Message.ShouldContain("Encrypted", Case.Insensitive);
        warning.Message.ShouldNotContain("password", Case.Insensitive);
        warning.Message.ShouldNotContain("Zytostatika");
    }

    [Fact]
    public void The_documents_metadata_and_annotations_are_never_read()
    {
        var pdf = TestPdfs.WithMetadata(Sentence + " Sie werden gekennzeichnet.", "Dr. Erika Mustermann", "https://example.org/intern/geheim");

        var extracted = Extract(pdf);

        extracted.Extraction.ShouldBe(PdfTextExtraction.Complete);
        var text = extracted.Text.ShouldNotBeNull().Value;
        text.ShouldBe(Sentence + " Sie werden gekennzeichnet.");
        text.ShouldNotContain("Mustermann");
        text.ShouldNotContain("example.org");
    }

    [Fact]
    public void Text_above_the_limit_is_cut_to_exactly_the_limit_and_truncated()
    {
        var pdf = TestPdfs.Long(40, "Zytostatika werden getrennt von anderen Arzneimitteln gelagert und gekennzeichnet.");

        var extracted = Extract(pdf);

        extracted.Extraction.ShouldBe(PdfTextExtraction.Truncated);
        var text = extracted.Text.ShouldNotBeNull().Value;
        text.Length.ShouldBe(PdfText.MaxLength);
        text.ShouldStartWith("Zytostatika werden getrennt");
    }

    [Fact]
    public void A_cut_that_would_split_a_surrogate_pair_leaves_the_pair_out()
    {
        // 51 letters, then emoji, each two UTF-16 characters: the pair at characters 199,999 and 200,000 straddles the cut.
        var pdf = TestPdfs.LettersThenEmoji(51, 100_000);

        var extracted = Extract(pdf);

        extracted.Extraction.ShouldBe(PdfTextExtraction.Truncated);
        var text = extracted.Text.ShouldNotBeNull().Value;
        text.Length.ShouldBe(PdfText.MaxLength - 1);
        char.IsLowSurrogate(text[^1]).ShouldBeTrue("the text ends after a whole pair");
        IsWellFormed(text).ShouldBeTrue();
    }

    [Fact]
    public void A_cut_between_two_surrogate_pairs_keeps_exactly_the_limit()
    {
        // 50 letters, then emoji: the pairs end at character 200,000.
        var pdf = TestPdfs.LettersThenEmoji(50, 100_000);

        var extracted = Extract(pdf);

        extracted.Extraction.ShouldBe(PdfTextExtraction.Truncated);
        var text = extracted.Text.ShouldNotBeNull().Value;
        text.Length.ShouldBe(PdfText.MaxLength);
        IsWellFormed(text).ShouldBeTrue();
    }

    [Fact]
    public void Text_up_to_the_limit_with_characters_outside_the_basic_plane_is_complete_and_kept()
    {
        var extracted = Extract(TestPdfs.LettersThenEmoji(60, 3));

        extracted.Extraction.ShouldBe(PdfTextExtraction.Complete);
        extracted.Text.ShouldNotBeNull().Value.ShouldBe(new string('A', 60) + string.Concat(Enumerable.Repeat("\U0001F600", 3)));
    }

    [Fact]
    public void Pages_after_the_500th_are_not_read_and_the_text_is_truncated()
    {
        var pdf = TestPdfs.NumberedPages(501);

        var extracted = Extract(pdf);

        extracted.Extraction.ShouldBe(PdfTextExtraction.Truncated);
        var text = extracted.Text.ShouldNotBeNull().Value;
        text.ShouldStartWith("Seite 1: ");
        text.ShouldContain("Seite 500: ");
        text.ShouldNotContain("Seite 501: ");
        text.ShouldEndWith("Seite 500: Zytostatika werden getrennt gelagert.");
    }

    [Fact]
    public void Exactly_500_pages_are_read_whole()
    {
        var extracted = Extract(TestPdfs.NumberedPages(500));

        extracted.Extraction.ShouldBe(PdfTextExtraction.Complete);
        extracted.Text.ShouldNotBeNull().Value.ShouldEndWith("Seite 500: Zytostatika werden getrennt gelagert.");
    }

    [Fact]
    public void Control_characters_and_nul_are_removed_line_breaks_become_newlines_and_the_text_is_trimmed()
    {
        // In the PDF string: two spaces, the sentence, \001 and \000 inside words, CR LF, a lone CR, a tab, and a
        // trailing CR LF and spaces.
        var pdf = TestPdfs.WithContentString(@"  Zytostatika\001 werden getrennt\000 gelagert.\015\012Kennzeichnung\015und Lagerung\011im Schrank der Apotheke.\015\012  ");

        var extracted = Extract(pdf);

        extracted.Extraction.ShouldBe(PdfTextExtraction.Complete);
        extracted.Text.ShouldNotBeNull().Value.ShouldBe("Zytostatika werden getrennt gelagert.\nKennzeichnung\nund Lagerung\tim Schrank der Apotheke.");
    }

    [Fact]
    public void A_cancelled_extraction_throws_and_is_not_reported_as_no_text()
    {
        var pdf = TestPdfs.NumberedPages(3);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Should.Throw<OperationCanceledException>(() => new PdfPigTextExtractor(logger).Extract(pdf, cancellation.Token));
        logger.Collector.Count.ShouldBe(0, "a cancellation is no unreadable file");
    }

    private static bool IsWellFormed(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(index), out _, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                return false;
            }

            index += consumed - 1;
        }

        return true;
    }

    private PdfChapterText Extract(byte[] pdf) => new PdfPigTextExtractor(logger).Extract(pdf, TestContext.Current.CancellationToken);
}
