using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// How a recorded PDF's bytes reach the extractor (#157): the whole blob, read as the version its lookup found, and
/// refused as not accepted when fewer bytes come back than that version had, since another upload replaced it.
/// </summary>
public class PdfChapterTextsTests
{
    private static readonly CourseVersionId Version = CourseVersionId.New();

    private readonly InMemoryStoredBlobs blobs = new();

    [Fact]
    public async Task The_whole_blob_is_read_and_the_extractors_outcome_answered()
    {
        var file = AddFileChapterTests.IssuedName(Version, ".pdf");
        byte[] content = [.. InMemoryStoredBlobs.PdfStart, .. "3 0 obj << /Type /Page >> endobj\n%%EOF\n"u8];
        blobs.Upload(file, "application/pdf", content.Length, content: content);
        var outcome = FakePdfTextExtractor.Complete("Zytostatika werden getrennt von anderen Arzneimitteln gelagert.");
        var reads = new FakePdfTextExtractor(outcome);

        var extracted = await PdfChapterTexts.ExtractAsync(blobs, reads, blobs.Current(file), TestContext.Current.CancellationToken);

        extracted.IsSuccess.ShouldBeTrue(extracted.IsFailure ? extracted.Error.Code : string.Empty);
        extracted.Value.ShouldBe(outcome);
        blobs.ReadCounts.ShouldBe([content.Length], "one read of exactly the blob's length");
        reads.Read.ShouldHaveSingleItem().ShouldBe(content);
    }

    [Fact]
    public async Task A_blob_replaced_since_it_was_found_is_not_accepted_and_never_reaches_the_extractor()
    {
        var file = AddFileChapterTests.IssuedName(Version, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);
        var found = blobs.Current(file);
        blobs.Upload(file, "application/pdf", 1024);
        var reads = new FakePdfTextExtractor(FakePdfTextExtractor.Complete("Zytostatika werden getrennt von anderen Arzneimitteln gelagert."));

        var extracted = await PdfChapterTexts.ExtractAsync(blobs, reads, found, TestContext.Current.CancellationToken);

        extracted.IsFailure.ShouldBeTrue();
        extracted.Error.Code.ShouldBe("editorial.upload-not-accepted");
        extracted.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        reads.Read.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scan_is_answered_as_no_text_not_refused()
    {
        var file = AddFileChapterTests.IssuedName(Version, ".pdf");
        blobs.Upload(file, "application/pdf", 512);

        var extracted = await PdfChapterTexts.ExtractAsync(blobs, new FakePdfTextExtractor(FakePdfTextExtractor.NoText), blobs.Current(file), TestContext.Current.CancellationToken);

        extracted.IsSuccess.ShouldBeTrue(extracted.IsFailure ? extracted.Error.Code : string.Empty);
        extracted.Value.Extraction.ShouldBe(PdfTextExtraction.NoText);
        extracted.Value.Text.ShouldBeNull();
    }
}
