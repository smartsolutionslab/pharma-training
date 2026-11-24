using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.PdfExtraction;

/// <summary>
/// Stands in for PdfPig behind the bounded extractor (#316): each call signals that it started, then blocks until the
/// test answers it (<see cref="Finish"/>) or fails it (<see cref="Fail"/>). It either honours the token, throwing
/// <see cref="OperationCanceledException"/> once it is cancelled as PdfPig does between pages, or ignores it, as one
/// pathological page does. It records how many calls entered and on what kind of thread each ran. Every call still
/// blocked is answered when it is disposed, so no test leaves a thread behind.
/// </summary>
internal sealed class BlockingPdfTextParser(bool honoursCancellation) : IPdfTextParser, IDisposable
{
    public static readonly PdfChapterText Answer = new(PdfTextExtraction.Complete, PdfText.From("Zytostatika werden getrennt von anderen Arzneimitteln gelagert."));

    private static readonly PdfChapterText NoText = new(PdfTextExtraction.NoText, null);

    private readonly Lock gate = new();
    private readonly List<Call> calls = [];
    private int entered;

    /// <summary>How many calls entered the parser.</summary>
    public int Entered => Volatile.Read(ref entered);

    /// <summary>Completes once the call numbered <paramref name="index"/> (from 0) has entered the parser.</summary>
    public Task Started(int index) => CallAt(index).Started.Task;

    /// <summary>Completes once the call numbered <paramref name="index"/> has left the parser, by answer, failure or cancellation.</summary>
    public Task Ended(int index) => CallAt(index).Ended.Task;

    /// <summary>Whether the call numbered <paramref name="index"/> ran on a thread of the thread pool.</summary>
    public bool RanOnThreadPool(int index) => CallAt(index).OnThreadPool;

    /// <summary>Whether the call numbered <paramref name="index"/> ran on a background thread, which never keeps the process alive.</summary>
    public bool RanOnBackgroundThread(int index) => CallAt(index).OnBackgroundThread;

    /// <summary>Lets the call numbered <paramref name="index"/> answer <paramref name="answer"/>.</summary>
    public void Finish(int index, PdfChapterText answer) => CallAt(index).Answer.TrySetResult(answer);

    /// <summary>Lets the call numbered <paramref name="index"/> throw <paramref name="exception"/>.</summary>
    public void Fail(int index, Exception exception) => CallAt(index).Answer.TrySetException(exception);

    public PdfChapterText Extract(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        var call = CallAt(Interlocked.Increment(ref entered) - 1);
        try
        {
            call.OnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
            call.OnBackgroundThread = Thread.CurrentThread.IsBackground;
            call.Started.TrySetResult();
            var answer = honoursCancellation ? call.Answer.Task.WaitAsync(cancellationToken) : call.Answer.Task;
            return answer.GetAwaiter().GetResult();
        }
        finally
        {
            call.Ended.TrySetResult();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var call in calls)
            {
                call.Answer.TrySetResult(NoText);
            }
        }
    }

    private Call CallAt(int index)
    {
        lock (gate)
        {
            while (calls.Count <= index)
            {
                calls.Add(new Call());
            }

            return calls[index];
        }
    }

    private sealed class Call
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<PdfChapterText> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool OnThreadPool { get; set; }

        public bool OnBackgroundThread { get; set; }
    }
}
