using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;

/// <summary>
/// Reads a PDF's text within the host's bounds (#316, ADR 0008 D11). At most <see cref="PdfTextLimits.MaxConcurrentParses"/>
/// parses run at once, each on a dedicated background thread, never a pool thread. A request that finds no slot within
/// <see cref="SlotWait"/> is refused as <see cref="PdfReadersBusy"/>, to retry after <see cref="RetryAfter"/>, and never
/// queued. A parse past <see cref="PdfTextLimits.Timeout"/> is answered as no text at once; its thread is told to stop,
/// whatever it produces later is ignored, and it keeps its slot until it really ends, so abandoned parses never exceed
/// the limit. Every wait runs on the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed partial class BoundedPdfTextExtractor : IPdfTextExtractor, IDisposable
{
    private static readonly PdfChapterText NoText = new(PdfTextExtraction.NoText, null);

    private readonly IPdfTextParser parser;
    private readonly PdfTextLimits limits;
    private readonly TimeProvider time;
    private readonly ILogger<BoundedPdfTextExtractor> logger;
    private readonly SemaphoreSlim slots;
    private readonly Lock gate = new();
    private bool disposed;

    public BoundedPdfTextExtractor(IPdfTextParser parser, PdfTextLimits limits, TimeProvider time, ILogger<BoundedPdfTextExtractor> logger)
    {
        Ensure.That(parser).IsNotNull();
        Ensure.That(limits).IsNotNull();
        Ensure.That(time).IsNotNull();
        Ensure.That(logger).IsNotNull();

        this.parser = parser;
        this.limits = limits;
        this.time = time;
        this.logger = logger;
        slots = new SemaphoreSlim(limits.MaxConcurrentParses, limits.MaxConcurrentParses);
    }

    /// <summary>How long a request waits for a free slot before it is refused: two editors saving at once, not a queue.</summary>
    public static TimeSpan SlotWait { get; } = TimeSpan.FromSeconds(1);

    /// <summary>When a refused request is told to try again: longer than a normal parse, much shorter than the deadline.</summary>
    public static TimeSpan RetryAfter { get; } = TimeSpan.FromSeconds(5);

    public async Task<Result<PdfChapterText, PdfReadersBusy>> ExtractAsync(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        // The slot wait starts now, before anything yields, so it runs from the moment the request asks.
        using (var slotWait = new CancellationTokenSource(SlotWait, time))
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, slotWait.Token);
            try
            {
                await slots.WaitAsync(waiting.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failure(new PdfReadersBusy(time.GetUtcNow() + RetryAfter));
            }
        }

        var read = new Read(this, pdf, cancellationToken);
        read.Start();
        var text = await read.Outcome.WaitAsync(cancellationToken);
        if (text is null)
        {
            Abandoned(logger, limits.Timeout);
            return Success(NoText);
        }

        return Success(text);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            slots.Dispose();
        }
    }

    /// <summary>Frees a slot; after disposal, at shutdown, an abandoned thread that ends frees nothing.</summary>
    private void Release()
    {
        lock (gate)
        {
            if (!disposed)
            {
                slots.Release();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A PDF was not read within {Timeout} and is recorded without text.")]
    private static partial void Abandoned(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "An abandoned PDF read ended after {Elapsed}.")]
    private static partial void AbandonedEnded(ILogger logger, TimeSpan elapsed);

    /// <summary>Error for running out of memory, the case ADR 0008 D11 leaves unbounded; Warning for any other failure.</summary>
    [LoggerMessage(Message = "An abandoned PDF read failed with {ExceptionType} after {Elapsed}.")]
    private static partial void AbandonedFailed(ILogger logger, LogLevel level, Type exceptionType, TimeSpan elapsed);

    /// <summary>
    /// One admitted parse: its deadline, its token (the request's and the deadline's), its thread, and its outcome, which
    /// completes with null at the deadline and with the parse's answer only if that comes first.
    /// </summary>
    private sealed class Read : IDisposable
    {
        private readonly BoundedPdfTextExtractor owner;
        private readonly ReadOnlyMemory<byte> pdf;
        private readonly CancellationToken request;
        private readonly CancellationTokenSource deadline;
        private readonly CancellationTokenSource parse;
        private readonly TaskCompletionSource<PdfChapterText?> outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly long started;

        public Read(BoundedPdfTextExtractor owner, ReadOnlyMemory<byte> pdf, CancellationToken request)
        {
            this.owner = owner;
            this.pdf = pdf;
            this.request = request;
            started = owner.time.GetTimestamp();
            deadline = new CancellationTokenSource(owner.limits.Timeout, owner.time);
            parse = CancellationTokenSource.CreateLinkedTokenSource(request, deadline.Token);
            deadline.Token.Register(() => outcome.TrySetResult(null));
        }

        /// <summary>Null when the parse was abandoned at its deadline.</summary>
        public Task<PdfChapterText?> Outcome => outcome.Task;

        public void Start()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "editorial-pdf-text" };
            try
            {
                thread.Start();
            }
            catch
            {
                End();
                throw;
            }
        }

        private void Run()
        {
            Exception? unheard = null;
            try
            {
                outcome.TrySetResult(owner.parser.Extract(pdf, parse.Token));
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !request.IsCancellationRequested)
            {
                outcome.TrySetResult(null);
            }
            catch (OperationCanceledException cancelled)
            {
                outcome.TrySetCanceled(cancelled.CancellationToken);
            }
            catch (Exception exception) when (request.IsCancellationRequested)
            {
                // The request was cancelled and no longer waits: the failure is logged here, and the outcome is
                // cancelled rather than faulted, so no task nobody awaits carries it.
                outcome.TrySetCanceled(request);
                unheard = exception;
            }
            catch (Exception exception)
            {
                // Whatever else a parse throws, a bug or running out of memory, is the caller's: it answers 500. Once
                // the deadline answered the caller, the outcome takes it no longer, and it is logged here instead.
                if (!outcome.TrySetException(exception))
                {
                    unheard = exception;
                }
            }
            finally
            {
                // The outcome is complete by now; null means the deadline answered it, not this thread.
                if (unheard is not null)
                {
                    var level = unheard is OutOfMemoryException ? LogLevel.Error : LogLevel.Warning;
                    var exceptionType = unheard.GetType();
                    var elapsed = owner.time.GetElapsedTime(started);
                    AbandonedFailed(owner.logger, level, exceptionType, elapsed);
                }
                else if (outcome.Task is { IsCompletedSuccessfully: true, Result: null })
                {
                    var elapsed = owner.time.GetElapsedTime(started);
                    AbandonedEnded(owner.logger, elapsed);
                }

                End();
            }
        }

        public void Dispose()
        {
            parse.Dispose();
            deadline.Dispose();
        }

        private void End()
        {
            Dispose();
            owner.Release();
        }
    }
}
