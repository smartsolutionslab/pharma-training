using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Stands in for PdfPig where a handler records a PDF chapter: answers the outcome a test gives it, and keeps the bytes
/// it was handed, so a test sees what was read and that it was asked at all. PdfPig itself is tested on real PDFs.
/// </summary>
internal sealed class FakePdfTextExtractor(PdfChapterText answer) : IPdfTextExtractor
{
    public FakePdfTextExtractor()
        : this(NoText)
    {
    }

    public static PdfChapterText NoText { get; } = new(PdfTextExtraction.NoText, null);

    /// <summary>The bytes of every PDF the extractor was handed, in the order handed.</summary>
    public List<byte[]> Read { get; } = [];

    public static PdfChapterText Complete(string text) => new(PdfTextExtraction.Complete, PdfText.From(text));

    public static PdfChapterText Truncated(string text) => new(PdfTextExtraction.Truncated, PdfText.From(text));

    public PdfChapterText Extract(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        Read.Add(pdf.ToArray());
        return answer;
    }
}
