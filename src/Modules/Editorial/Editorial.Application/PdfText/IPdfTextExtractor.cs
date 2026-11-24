namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;

/// <summary>
/// Reads the text of a PDF's pages when a PDF chapter is recorded (ADR 0008 D11): pure work over the bytes, which the
/// module reads from its storage itself. A PDF that cannot be read, or is not read within its deadline, is no error but
/// a PDF without text. While every reader is busy, the PDF is refused for now (#316).
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// The PDF's page text and whether it was whole, cut at a limit or not found; or <see cref="PdfReadersBusy"/> when
    /// no reader was free in time. Cancellation propagates.
    /// </summary>
    Task<Result<PdfChapterText, PdfReadersBusy>> ExtractAsync(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken);
}
