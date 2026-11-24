using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;

/// <summary>
/// Parses a PDF's page text, synchronously and without bounds of its own (ADR 0008 D11): <see cref="BoundedPdfTextExtractor"/>
/// runs it on a dedicated thread, within its slots and deadline (#316).
/// </summary>
public interface IPdfTextParser
{
    /// <summary>The PDF's page text and whether it was whole, cut at a limit or not found; cancellation propagates.</summary>
    PdfChapterText Extract(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken);
}
