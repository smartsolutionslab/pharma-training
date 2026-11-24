namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;

/// <summary>
/// Every PDF reader of the host is busy (#316): the PDF is refused for now, to try again from <paramref name="RetryAt"/>.
/// It concerns the host's capacity, not a domain rule, so it lives beside the port that returns it; it is a
/// <see cref="DomainError"/> only because <see cref="ApiError.RetryLater"/> takes one. Its message is fixed and reads
/// nothing of the request (ADR 0027).
/// </summary>
public sealed record PdfReadersBusy(DateTimeOffset RetryAt)
    : DomainError("editorial.pdf-readers-busy", "Too many PDFs are being read right now; try again in a few seconds.");
