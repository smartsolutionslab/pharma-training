using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Stands in for the bounded PdfPig reader where a handler records a PDF chapter: answers the outcome a test gives it,
/// or refuses as busy (<see cref="Busy"/>), and keeps the bytes it was handed, so a test sees what was read and that it
/// was asked at all. PdfPig and the bound are tested on their own.
/// </summary>
internal sealed class FakePdfTextExtractor : IPdfTextExtractor
{
    private readonly Result<PdfChapterText, PdfReadersBusy> answer;

    public FakePdfTextExtractor()
        : this(NoText)
    {
    }

    public FakePdfTextExtractor(PdfChapterText answer)
        : this(Success(answer))
    {
    }

    private FakePdfTextExtractor(Result<PdfChapterText, PdfReadersBusy> answer) => this.answer = answer;

    public static PdfChapterText NoText { get; } = new(PdfTextExtraction.NoText, null);

    /// <summary>The bytes of every PDF the extractor was handed, in the order handed.</summary>
    public List<byte[]> Read { get; } = [];

    public static PdfChapterText Complete(string text) => new(PdfTextExtraction.Complete, PdfText.From(text));

    public static PdfChapterText Truncated(string text) => new(PdfTextExtraction.Truncated, PdfText.From(text));

    /// <summary>An extractor whose readers are all busy: every PDF is refused, to retry at <paramref name="retryAt"/>.</summary>
    public static FakePdfTextExtractor Busy(DateTimeOffset retryAt) => new(Failure(new PdfReadersBusy(retryAt)));

    public Task<Result<PdfChapterText, PdfReadersBusy>> ExtractAsync(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        Read.Add(pdf.ToArray());
        return Task.FromResult(answer);
    }
}
