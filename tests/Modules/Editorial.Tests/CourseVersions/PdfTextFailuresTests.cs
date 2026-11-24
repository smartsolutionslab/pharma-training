using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Every PDF reader busy is a refusal for now, not for good (#316): 429 with the module's code, one fixed English
/// message that names nothing of the request (ADR 0027), and the instant from which to retry (ADR 0020).
/// </summary>
public class PdfTextFailuresTests
{
    [Fact]
    public void Busy_pdf_readers_are_a_429_retry_later_with_their_code_message_and_instant()
    {
        var retryAt = new DateTimeOffset(2026, 11, 24, 9, 0, 5, TimeSpan.Zero);

        var mapped = PdfTextFailures.From(new PdfReadersBusy(retryAt));

        mapped.Code.ShouldBe("editorial.pdf-readers-busy");
        mapped.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        mapped.Message.ShouldBe("Too many PDFs are being read right now; try again in a few seconds.");
        mapped.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(retryAt);
    }

    [Fact]
    public void The_busy_message_is_the_same_whenever_it_is_given()
    {
        var early = PdfTextFailures.From(new PdfReadersBusy(new DateTimeOffset(2026, 11, 24, 9, 0, 5, TimeSpan.Zero)));
        var late = PdfTextFailures.From(new PdfReadersBusy(new DateTimeOffset(2027, 3, 1, 17, 30, 0, TimeSpan.Zero)));

        early.Message.ShouldBe(late.Message);
    }
}
