namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;

/// <summary>What a caller sees when a PDF's text cannot be read now (#316).</summary>
public static class PdfTextFailures
{
    /// <summary>Busy readers are a refusal for now: 429, and the instant from which to retry (ADR 0020).</summary>
    public static ApiError From(PdfReadersBusy busy)
    {
        Ensure.That(busy).IsNotNull();
        return ApiError.RetryLater(busy, busy.RetryAt);
    }
}
