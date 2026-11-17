// Learning service — Domain (module.style: event-sourced, module.event-store: marten, module.ddd-depth: strict)
// One file for review; in the project every type lives in its own file under Domain/LearningSessions/.
// Events take their value objects by constructor and hold no names: a person is only a PersonId (GDPR).

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

// ───────────────────────────── Identities ─────────────────────────────

public sealed record LearningSessionId : IValueObject<Guid>, IAggregateId
{
    private LearningSessionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static LearningSessionId New() => new(Guid.CreateVersion7());

    public static LearningSessionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new LearningSessionId(value);
    }
}

// PersonId, CourseId, CourseVersionId, ChapterId: same shape (From(Guid), refuses Guid.Empty).

// ───────────────────────────── Values ─────────────────────────────

/// <summary>How far a learner got in one chapter: watched share of a video, scrolled share of a text, pages seen of a PDF.</summary>
public sealed record ProgressPercent : IValueObject<int>, IComparable<ProgressPercent>
{
    private ProgressPercent(int value) => Value = value;

    public int Value { get; }

    public static ProgressPercent None => new(0);

    public static ProgressPercent Complete => new(100);

    public static ProgressPercent From(int value)
    {
        Ensure.That(value).InRange(0, 100);
        return new ProgressPercent(value);
    }

    public int CompareTo(ProgressPercent? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator >(ProgressPercent left, ProgressPercent right) => left.CompareTo(right) > 0;

    public static bool operator <(ProgressPercent left, ProgressPercent right) => left.CompareTo(right) < 0;

    public static bool operator >=(ProgressPercent left, ProgressPercent right) => left.CompareTo(right) >= 0;

    public static bool operator <=(ProgressPercent left, ProgressPercent right) => left.CompareTo(right) <= 0;
}

public enum ChapterKind
{
    Text,
    Pdf,
    Video,
}

public enum SessionStatus
{
    InProgress,
    ContentCompleted,
    Finished,
}

/// <summary>When a chapter counts as worked through. Video: 95 % (seeking is limited to what was seen); text and PDF: the end.</summary>
public static class CompletionRule
{
    public static ProgressPercent ThresholdFor(ChapterKind kind) =>
        kind == ChapterKind.Video ? ProgressPercent.From(95) : ProgressPercent.Complete;
}

// ───────────────────────────── Aggregate ─────────────────────────────

/// <summary>
/// One learner working through one course version, from the first chapter until the test is passed.
/// A new year's repetition is a new session; a finished one never changes again.
/// </summary>
public sealed class LearningSession : EventSourcedAggregate<LearningSessionId>
{
    private readonly Dictionary<ChapterId, ChapterKind> chapters = [];
    private readonly Dictionary<ChapterId, ProgressPercent> progress = [];
    private readonly HashSet<ChapterId> completed = [];

    private LearningSession()
    {
    }

    public PersonId Learner { get; private set; } = default!;

    public CourseId Course { get; private set; } = default!;

    public CourseVersionId CourseVersion { get; private set; } = default!;

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
        var session = new LearningSession();
        session.Raise(new SessionStarted(id, learner, course, courseVersion, textChapters, pdfChapters, videoChapters, now));
        return session;
    }

    /// <summary>
    /// The client reports progress in batches (on pause, at the end of a chapter, at most every 30 s).
    /// Only real progress is stored, so a replayed or older report changes nothing.
    /// </summary>
    public Result<Unit, DomainError> RecordProgress(ChapterId chapter, ProgressPercent reported, Timestamp now)
    {
        if (Status == SessionStatus.Finished)
        {
            return Failure(new SessionFinishedAlready(Id));
        }

        if (!chapters.TryGetValue(chapter, out var kind))
        {
            return Failure(new ChapterNotInCourse(Id, chapter));
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
        if (Status == SessionStatus.Finished)
        {
            return Success(Unit.Value); // a passed test outranks a late lock
        }

        var toReopen = failedChapters.Where(completed.Contains).Distinct().ToList();
        if (toReopen.Count == 0)
        {
            return Success(Unit.Value); // AttemptsExhausted delivered twice
        }

        Raise(new ChaptersReopened(Id, toReopen, now));
        return Success(Unit.Value);
    }

    /// <summary>The test was passed (TestPassed). Idempotent.</summary>
    public void Finish(Timestamp now)
    {
        if (Status != SessionStatus.Finished)
        {
            Raise(new SessionFinished(Id, now));
        }
    }

    // ── Apply: the only place state changes; replaying the stream rebuilds the same session ──

    private void Apply(SessionStarted started)
    {
        Id = started.Session;
        Learner = started.Learner;
        Course = started.Course;
        CourseVersion = started.CourseVersion;
        Status = SessionStatus.InProgress;

        foreach (var chapter in started.TextChapters) chapters[chapter] = ChapterKind.Text;
        foreach (var chapter in started.PdfChapters) chapters[chapter] = ChapterKind.Pdf;
        foreach (var chapter in started.VideoChapters) chapters[chapter] = ChapterKind.Video;
        foreach (var chapter in chapters.Keys) progress[chapter] = ProgressPercent.None;
    }

    private void Apply(ChapterProgressRecorded recorded) => progress[recorded.Chapter] = recorded.Progress;

    private void Apply(ChapterCompleted chapterCompleted) => completed.Add(chapterCompleted.Chapter);

    private void Apply(ContentCompleted _) => Status = SessionStatus.ContentCompleted;

    private void Apply(ChaptersReopened reopened)
    {
        foreach (var chapter in reopened.Chapters)
        {
            completed.Remove(chapter);
            progress[chapter] = ProgressPercent.None;
        }

        Status = SessionStatus.InProgress;
    }

    private void Apply(SessionFinished _) => Status = SessionStatus.Finished;
}

// ───────────────────────────── Domain events (stored forever) ─────────────────────────────
// SessionStarted → LearningSessionStartedV1, ContentCompleted → ContentCompletedV1 (integration, via outbox).

public sealed record SessionStarted(
    LearningSessionId Session,
    PersonId Learner,
    CourseId Course,
    CourseVersionId CourseVersion,
    IReadOnlyList<ChapterId> TextChapters,
    IReadOnlyList<ChapterId> PdfChapters,
    IReadOnlyList<ChapterId> VideoChapters,
    Timestamp StartedAt) : IDomainEvent;

public sealed record ChapterProgressRecorded(LearningSessionId Session, ChapterId Chapter, ProgressPercent Progress, Timestamp RecordedAt) : IDomainEvent;

public sealed record ChapterCompleted(LearningSessionId Session, ChapterId Chapter, Timestamp CompletedAt) : IDomainEvent;

public sealed record ContentCompleted(LearningSessionId Session, PersonId Learner, CourseId Course, CourseVersionId CourseVersion, Timestamp CompletedAt) : IDomainEvent;

public sealed record ChaptersReopened(LearningSessionId Session, IReadOnlyList<ChapterId> Chapters, Timestamp ReopenedAt) : IDomainEvent;

public sealed record SessionFinished(LearningSessionId Session, Timestamp FinishedAt) : IDomainEvent;

// ───────────────────────────── Domain errors ─────────────────────────────

public sealed record SessionFinishedAlready(LearningSessionId Session)
    : DomainError("learning.session-finished", "Diese Schulung ist bereits abgeschlossen.");

public sealed record ChapterNotInCourse(LearningSessionId Session, ChapterId Chapter)
    : DomainError("learning.chapter-not-in-course", "Dieses Kapitel gehört nicht zu dieser Schulungsversion.");
