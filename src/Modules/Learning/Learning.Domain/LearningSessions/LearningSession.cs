using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>
/// One learner working through one course version, from the first chapter until the test is passed.
/// A new year's repetition is a new session; a finished one never changes again.
/// </summary>
public sealed class LearningSession : EventSourcedAggregate<LearningSessionId>
{
    private readonly Dictionary<ChapterId, ChapterKind> chapters = [];
    private readonly Dictionary<ChapterId, ProgressPercent> progress = [];
    private readonly HashSet<ChapterId> completed = [];

    // Only loading creates an empty session, which its first event fills.
    private LearningSession()
    {
    }

    public PersonId Learner { get; private set; } = null!;

    public CourseId Course { get; private set; } = null!;

    public CourseVersionId CourseVersion { get; private set; } = null!;

    public SessionStatus Status { get; private set; }

    public IReadOnlyCollection<ChapterId> CompletedChapters => completed;

    /// <summary>
    /// Starts a session. "At most one unfinished session per learner and course version" spans streams:
    /// the Application looks it up first (a Marten projection keyed by learner and version).
    /// </summary>
    public static LearningSession Start(
        LearningSessionId id,
        PersonId learner,
        CourseId course,
        CourseVersionId courseVersion,
        IReadOnlyList<ChapterId> textChapters,
        IReadOnlyList<ChapterId> pdfChapters,
        IReadOnlyList<ChapterId> videoChapters,
        Timestamp now)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(learner).IsNotNull();
        Ensure.That(course).IsNotNull();
        Ensure.That(courseVersion).IsNotNull();
        Ensure.That(textChapters).IsNotNull();
        Ensure.That(pdfChapters).IsNotNull();
        Ensure.That(videoChapters).IsNotNull();
        Ensure.That(now).IsNotNull();

        var session = new LearningSession();
        session.Raise(new SessionStarted(id, learner, course, courseVersion, textChapters, pdfChapters, videoChapters, now));
        return session;
    }

    /// <summary>
    /// The client reports progress in batches (on pause, at the end of a chapter, at most every 30 s).
    /// Only real progress is stored, so a replayed or older report changes nothing.
    /// </summary>
    public Result<Unit, RecordProgressError> RecordProgress(ChapterId chapter, ProgressPercent reported, Option<VideoDuration> duration, Timestamp now)
    {
        Ensure.That(chapter).IsNotNull();
        Ensure.That(reported).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (Status == SessionStatus.Finished)
        {
            return Failure(LearningSessionErrors.FinishedAlready(Id));
        }

        if (!chapters.TryGetValue(chapter, out var kind))
        {
            return Failure(LearningSessionErrors.ChapterNotInCourse(Id, chapter));
        }

        if (reported <= progress[chapter])
        {
            return Success(Unit.Value);
        }

        Raise(new ChapterProgressRecorded(Id, chapter, reported, now));

        if (!completed.Contains(chapter) && reported >= CompletionRule.ThresholdFor(kind))
        {
            Raise(new ChapterCompleted(Id, chapter, now));
        }

        if (Status == SessionStatus.InProgress && completed.Count == chapters.Count)
        {
            Raise(new ContentCompleted(Id, Learner, Course, CourseVersion, now));
        }

        return Success(Unit.Value);
    }

    /// <summary>
    /// Three failed attempts (AttemptsExhausted): the chapters of the wrongly answered questions open again,
    /// and the test unlocks only once they are worked through anew.
    /// </summary>
    public Result<Unit, DomainError> ReopenChapters(IReadOnlyList<ChapterId> failedChapters, Timestamp now)
    {
        Ensure.That(failedChapters).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (Status == SessionStatus.Finished)
        {
            // A passed test outranks a late lock.
            return Success(Unit.Value);
        }

        var toReopen = failedChapters.Where(completed.Contains).Distinct().ToList();
        if (toReopen.Count == 0)
        {
            // AttemptsExhausted delivered twice.
            return Success(Unit.Value);
        }

        Raise(new ChaptersReopened(Id, toReopen, now));
        return Success(Unit.Value);
    }

    /// <summary>The test was passed (TestPassed). Idempotent.</summary>
    public void Finish(Timestamp now)
    {
        Ensure.That(now).IsNotNull();

        if (Status != SessionStatus.Finished)
        {
            Raise(new SessionFinished(Id, now));
        }
    }

    // Apply: the only place state changes; replaying the stream rebuilds the same session.
    private void Apply(SessionStarted started)
    {
        Id = started.Session;
        Learner = started.Learner;
        Course = started.Course;
        CourseVersion = started.CourseVersion;
        Status = SessionStatus.InProgress;

        foreach (var chapter in started.TextChapters)
        {
            chapters[chapter] = ChapterKind.Text;
        }

        foreach (var chapter in started.PdfChapters)
        {
            chapters[chapter] = ChapterKind.Pdf;
        }

        foreach (var chapter in started.VideoChapters)
        {
            chapters[chapter] = ChapterKind.Video;
        }

        foreach (var chapter in chapters.Keys)
        {
            progress[chapter] = ProgressPercent.None;
        }
    }

    private void Apply(ChapterProgressRecorded recorded) => progress[recorded.Chapter] = recorded.Progress;

    private void Apply(ChapterCompleted chapterCompleted) => completed.Add(chapterCompleted.Chapter);

    private void Apply(ContentCompleted contentCompleted) => Status = SessionStatus.ContentCompleted;

    private void Apply(ChaptersReopened reopened)
    {
        foreach (var chapter in reopened.Chapters)
        {
            completed.Remove(chapter);
            progress[chapter] = ProgressPercent.None;
        }

        Status = SessionStatus.InProgress;
    }

    private void Apply(SessionFinished finished) => Status = SessionStatus.Finished;
}
