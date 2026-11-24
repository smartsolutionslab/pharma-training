using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.PdfExtraction;

/// <summary>
/// Reading a PDF's text is bounded in concurrency and time (#316, ADR 0008 D11): at most the limit's parses run at once,
/// each on a dedicated background thread; a request that finds no slot within <see cref="BoundedPdfTextExtractor.SlotWait"/>
/// is refused as busy, to retry after <see cref="BoundedPdfTextExtractor.RetryAfter"/>, never queued; a parse past its
/// deadline is answered as no text at once, while its thread keeps its slot until it really ends. Time runs on a
/// <see cref="FakeTimeProvider"/>; every test waits for conditions, never for a delay.
/// </summary>
public sealed class BoundedPdfTextExtractorTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly byte[] Pdf = [.. "%PDF-1.4\n"u8];

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));
    private readonly BlockingPdfTextParser honouring = new(honoursCancellation: true);
    private readonly BlockingPdfTextParser ignoring = new(honoursCancellation: false);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        honouring.Dispose();
        ignoring.Dispose();
    }

    [Fact]
    public void A_request_waits_a_second_for_a_slot_and_is_told_to_retry_after_five_seconds()
    {
        BoundedPdfTextExtractor.SlotWait.ShouldBe(TimeSpan.FromSeconds(1));
        BoundedPdfTextExtractor.RetryAfter.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Within_the_limit_the_parsers_answer_is_returned()
    {
        var extractor = Bounded(honouring, 2);

        var extracting = extractor.ExtractAsync(Pdf, Cancellation);
        await honouring.Started(0);
        honouring.Finish(0, BlockingPdfTextParser.Answer);
        var extracted = await extracting;

        extracted.IsSuccess.ShouldBeTrue();
        extracted.Value.ShouldBe(BlockingPdfTextParser.Answer);
    }

    [Fact]
    public async Task The_parse_runs_on_a_dedicated_background_thread_not_on_the_thread_pool()
    {
        var extractor = Bounded(honouring, 2);

        var extracting = extractor.ExtractAsync(Pdf, Cancellation);
        await honouring.Started(0);
        honouring.Finish(0, BlockingPdfTextParser.Answer);
        await extracting;

        honouring.RanOnThreadPool(0).ShouldBeFalse("a parse blocks its thread for its whole length: never a pool thread's");
        honouring.RanOnBackgroundThread(0).ShouldBeTrue("an abandoned parse must never keep the host from stopping");
    }

    [Fact]
    public async Task With_every_slot_taken_a_further_request_is_refused_as_busy_once_the_slot_wait_passes_without_reaching_the_parser()
    {
        var extractor = Bounded(honouring, 2);
        var first = extractor.ExtractAsync(Pdf, Cancellation);
        var second = extractor.ExtractAsync(Pdf, Cancellation);
        await honouring.Started(0);
        await honouring.Started(1);

        var third = extractor.ExtractAsync(Pdf, Cancellation);
        time.Advance(BoundedPdfTextExtractor.SlotWait);
        var refused = await third;

        refused.IsFailure.ShouldBeTrue("the third request must be refused, not queued behind the two parses");
        refused.Error.ShouldBeOfType<PdfReadersBusy>().RetryAt.ShouldBe(time.GetUtcNow() + BoundedPdfTextExtractor.RetryAfter);
        refused.Error.Code.ShouldBe("editorial.pdf-readers-busy");
        honouring.Entered.ShouldBe(2);
        first.IsCompleted.ShouldBeFalse();
        second.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task A_request_whose_slot_frees_within_the_slot_wait_is_parsed()
    {
        var extractor = Bounded(honouring, 2);
        var first = extractor.ExtractAsync(Pdf, Cancellation);
        var second = extractor.ExtractAsync(Pdf, Cancellation);
        await honouring.Started(0);
        await honouring.Started(1);

        var third = extractor.ExtractAsync(Pdf, Cancellation);
        honouring.Finish(0, BlockingPdfTextParser.Answer);
        await honouring.Started(2);
        honouring.Finish(2, BlockingPdfTextParser.Answer);
        honouring.Finish(1, BlockingPdfTextParser.Answer);

        (await third).IsSuccess.ShouldBeTrue("the third request had a slot within its wait");
        (await first).IsSuccess.ShouldBeTrue();
        (await second).IsSuccess.ShouldBeTrue();
        honouring.Entered.ShouldBe(3);
    }

    [Fact]
    public async Task A_request_cancelled_while_it_waits_for_a_slot_is_cancelled_not_refused_as_busy()
    {
        var extractor = Bounded(honouring, 1);
        var first = extractor.ExtractAsync(Pdf, Cancellation);
        await honouring.Started(0);
        using var cancellation = new CancellationTokenSource();

        var waiting = extractor.ExtractAsync(Pdf, cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(waiting);
        honouring.Entered.ShouldBe(1);
        honouring.Finish(0, BlockingPdfTextParser.Answer);
        (await first).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_parser_failure_other_than_cancellation_propagates_as_a_bug()
    {
        var extractor = Bounded(honouring, 2);

        var extracting = extractor.ExtractAsync(Pdf, Cancellation);
        await honouring.Started(0);
        honouring.Fail(0, new InvalidOperationException("bug"));

        await Should.ThrowAsync<InvalidOperationException>(extracting);
    }

    [Fact]
    public async Task Running_out_of_memory_in_a_parse_propagates_and_is_never_recorded_as_no_text()
    {
        var extractor = Bounded(honouring, 2);

        var extracting = extractor.ExtractAsync(Pdf, Cancellation);
        await honouring.Started(0);
        honouring.Fail(0, new OutOfMemoryException());

        await Should.ThrowAsync<OutOfMemoryException>(extracting);
    }

    private BoundedPdfTextExtractor Bounded(IPdfTextParser parser, int maxConcurrentParses) =>
        new(parser, PdfTextLimits.Of(maxConcurrentParses, Timeout), time, NullLogger<BoundedPdfTextExtractor>.Instance);
}
