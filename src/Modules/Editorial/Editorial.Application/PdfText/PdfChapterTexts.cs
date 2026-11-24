using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;

/// <summary>Reads a recorded PDF's whole file from the module's storage and hands it to the extractor (ADR 0008 D11).</summary>
public static class PdfChapterTexts
{
    /// <summary>
    /// The text of the PDF <paramref name="pdf"/> names, read as the version its lookup found (at most
    /// <see cref="ChapterUploads.MaxPdfBytes"/>, which <see cref="ChapterUploads.AcceptAsync"/> checked). Fewer bytes than
    /// it had mean another upload replaced it since: not accepted, and the extractor is never asked. Readers all busy
    /// refuse it for now, to retry later (#316).
    /// </summary>
    public static async Task<Result<PdfChapterText, ApiError>> ExtractAsync(IStoredBlobs blobs, IPdfTextExtractor extractor, StoredBlob pdf, CancellationToken cancellationToken)
    {
        Ensure.That(blobs).IsNotNull();
        Ensure.That(extractor).IsNotNull();
        Ensure.That(pdf).IsNotNull();

        var content = await blobs.ReadStartAsync(pdf, checked((int)pdf.Length), cancellationToken);
        if (content.Length < pdf.Length)
        {
            return Failure(CourseVersionFailures.UploadNotAccepted());
        }

        var extracted = await extractor.ExtractAsync(content, cancellationToken);
        if (extracted.IsFailure)
        {
            return Failure(PdfTextFailures.From(extracted.Error));
        }

        return Success(extracted.Value);
    }
}
