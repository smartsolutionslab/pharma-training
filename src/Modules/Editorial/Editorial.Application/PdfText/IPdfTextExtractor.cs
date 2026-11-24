namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;

/// <summary>
/// Reads the text of a PDF's pages when a PDF chapter is recorded (ADR 0008 D11): pure work over the bytes, which the
/// module reads from its storage itself. A PDF that cannot be read is no error but a PDF without text.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>The PDF's page text and whether it was whole, cut at a limit or not found; cancellation propagates.</summary>
    PdfChapterText Extract(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken);
}
