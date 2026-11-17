// Assessment service — Domain (module.style: event-sourced, module.event-store: marten, module.ddd-depth: strict)
// One file for review; in the project every type lives in its own file under Domain/Examinations/.
// The aggregate is the examination per learning session, not the single attempt:
// "at most three attempts" spans all attempts, so they share one consistency boundary.

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

// ───────────────────────────── Identities ─────────────────────────────

public sealed record ExaminationId : IValueObject<Guid>, IAggregateId
{
    private ExaminationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ExaminationId New() => new(Guid.CreateVersion7());

    public static ExaminationId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new ExaminationId(value);
    }
}

// AttemptId, LearningSessionId, PersonId, CourseId, CourseVersionId, QuestionId, QuestionVersionId,
// AnswerOptionId, ChapterId: same shape (From(Guid), refuses Guid.Empty).

// ───────────────────────────── Values ─────────────────────────────

public sealed record ScorePercent : IValueObject<int>, IComparable<ScorePercent>
{
    private ScorePercent(int value) => Value = value;

    public int Value { get; }

    public static ScorePercent From(int value)
    {
        Ensure.That(value).InRange(0, 100);
        return new ScorePercent(value);
    }

    public static ScorePercent Of(QuestionCount correct, QuestionCount total)
    {
        Ensure.That(total.Value).AtLeast(1);
        Ensure.That(correct.Value).InRange(0, total.Value);
        return new ScorePercent((int)Math.Floor(100m * correct.Value / total.Value));
    }

    public int CompareTo(ScorePercent? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator >=(ScorePercent left, ScorePercent right) => left.CompareTo(right) >= 0;

    public static bool operator <=(ScorePercent left, ScorePercent right) => left.CompareTo(right) <= 0;

    public static bool operator >(ScorePercent left, ScorePercent right) => left.CompareTo(right) > 0;

    public static bool operator <(ScorePercent left, ScorePercent right) => left.CompareTo(right) < 0;
}

public sealed record QuestionCount : IValueObject<int>
{
    private QuestionCount(int value) => Value = value;

    public int Value { get; }

    public static QuestionCount From(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new QuestionCount(value);
    }
}

/// <summary>Which attempt this is: 1, 2 or 3.</summary>
public sealed record AttemptNumber : IValueObject<int>, IComparable<AttemptNumber>
{
    private AttemptNumber(int value) => Value = value;

    public int Value { get; }

    public static AttemptNumber First => new(1);

    public static AttemptNumber From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new AttemptNumber(value);
    }

    public AttemptNumber Next() => new(Value + 1);

    public int CompareTo(AttemptNumber? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator >=(AttemptNumber left, AttemptNumber right) => left.CompareTo(right) >= 0;

    public static bool operator <=(AttemptNumber left, AttemptNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >(AttemptNumber left, AttemptNumber right) => left.CompareTo(right) > 0;

    public static bool operator <(AttemptNumber left, AttemptNumber right) => left.CompareTo(right) < 0;
}

public enum ExaminationStatus
{
    Locked,       // content not (yet again) worked through
    Unlocked,     // an attempt may start
    InAttempt,    // an attempt is open
    Passed,       // terminal
    Exhausted,    // three failed attempts; unlocks only after ContentCompleted
}

public enum AttemptOutcome
{
    Passed,
    Failed,
}

// ───────────────────────────── Test definition (from TestDefinitionPublished) ─────────────────────────────
// A local copy per course version, read by the Application and handed to the aggregate. Never sent to the frontend.

public sealed record DefinedQuestion(
    QuestionVersionId Question,
    ChapterId Chapter,
    IReadOnlyList<AnswerOptionId> Options,
    IReadOnlyList<AnswerOptionId> CorrectOptions);

public sealed record TestDefinition(
    CourseVersionId CourseVersion,
    IReadOnlyList<DefinedQuestion> Questions,
    ScorePercent PassingScore,
    AttemptNumber MaxAttempts)
{
    /// <summary>A question counts as right only if exactly the correct options were chosen (single and multiple choice).</summary>
    public AnswerVerdict Judge(QuestionVersionId question, IReadOnlyCollection<AnswerOptionId> chosen)
    {
        var defined = Questions.Single(candidate => candidate.Question == question);
        return defined.CorrectOptions.ToHashSet().SetEquals(chosen) ? AnswerVerdict.Right : AnswerVerdict.Wrong;
    }

    public ChapterId ChapterOf(QuestionVersionId question) => Questions.Single(candidate => candidate.Question == question).Chapter;
}

public enum AnswerVerdict
{
    Right,
    Wrong,
}

// ───────────────────────────── Entity ─────────────────────────────

/// <summary>One run through the test. Questions and options are shuffled per attempt; the order is stored.</summary>
public sealed class TestAttempt
{
    private readonly Dictionary<QuestionVersionId, IReadOnlyList<AnswerOptionId>> answers = [];

    internal TestAttempt(AttemptId id, AttemptNumber number, IReadOnlyList<QuestionVersionId> questions)
    {
        Id = id;
        Number = number;
        Questions = questions;
    }

    public AttemptId Id { get; }

    public AttemptNumber Number { get; }

    public IReadOnlyList<QuestionVersionId> Questions { get; }

    public AttemptOutcome? Outcome { get; private set; }

    public ScorePercent? Score { get; private set; }

    internal IReadOnlyDictionary<QuestionVersionId, IReadOnlyList<AnswerOptionId>> Answers => answers;

    internal void Answer(QuestionVersionId question, IReadOnlyList<AnswerOptionId> chosen) => answers[question] = chosen;

    internal void Close(ScorePercent score, AttemptOutcome outcome)
    {
        Score = score;
        Outcome = outcome;
    }
}

// ───────────────────────────── Aggregate ─────────────────────────────

public sealed class Examination : EventSourcedAggregate<ExaminationId>
{
    private readonly List<TestAttempt> attempts = [];

    private Examination()
    {
    }

    public LearningSessionId Session { get; private set; } = default!;

    public PersonId Learner { get; private set; } = default!;

    public CourseId Course { get; private set; } = default!;

    public CourseVersionId CourseVersion { get; private set; } = default!;

    public ExaminationStatus Status { get; private set; }

    public IReadOnlyList<TestAttempt> Attempts => attempts;

    private TestAttempt? OpenAttempt => attempts.LastOrDefault(attempt => attempt.Outcome is null);

    /// <summary>First ContentCompleted of a session: the examination exists and is unlocked.</summary>
    public static Examination Open(
        ExaminationId id,
        LearningSessionId session,
        PersonId learner,
        CourseId course,
        CourseVersionId courseVersion,
        Timestamp now)
    {
        var examination = new Examination();
        examination.Raise(new ExaminationOpened(id, session, learner, course, courseVersion, now));
        return examination;
    }

    /// <summary>ContentCompleted again after the reopened chapters were worked through: three new attempts.</summary>
    public void Unlock(Timestamp now)
    {
        if (Status == ExaminationStatus.Exhausted)
        {
            Raise(new ExaminationUnlocked(Id, now));
        }
    }

    /// <param name="shuffledQuestions">The definition's questions in this attempt's random order (shuffled by the Application).</param>
    public Result<AttemptId, DomainError> StartAttempt(AttemptId attempt, IReadOnlyList<QuestionVersionId> shuffledQuestions, Timestamp now)
    {
        if (Status != ExaminationStatus.Unlocked)
        {
            return Failure(new ExaminationNotUnlocked(Id, Status));
        }

        var number = attemptsSinceUnlock.Count == 0 ? AttemptNumber.First : attemptsSinceUnlock[^1].Number.Next();
        Raise(new AttemptStarted(Id, attempt, number, shuffledQuestions, now));
        return Success(attempt);
    }

    public Result<Unit, DomainError> Answer(QuestionVersionId question, IReadOnlyList<AnswerOptionId> chosen, Timestamp now)
    {
        if (OpenAttempt is not { } attempt)
        {
            return Failure(new NoOpenAttempt(Id));
        }

        if (!attempt.Questions.Contains(question))
        {
            return Failure(new QuestionNotInAttempt(Id, question));
        }

        Raise(new QuestionAnswered(Id, attempt.Id, question, chosen, now));
        return Success(Unit.Value);
    }

    /// <summary>Scores the open attempt on the server against the version's definition. Unanswered questions are wrong.</summary>
    public Result<AttemptOutcome, DomainError> Submit(TestDefinition definition, Timestamp now)
    {
        if (OpenAttempt is not { } attempt)
        {
            return Failure(new NoOpenAttempt(Id));
        }

        if (definition.CourseVersion != CourseVersion)
        {
            return Failure(new DefinitionOfAnotherVersion(Id, definition.CourseVersion));
        }

        var wrong = attempt.Questions
            .Where(question => !attempt.Answers.TryGetValue(question, out var chosen)
                               || definition.Judge(question, chosen) == AnswerVerdict.Wrong)
            .ToList();

        var score = ScorePercent.Of(
            QuestionCount.From(attempt.Questions.Count - wrong.Count),
            QuestionCount.From(attempt.Questions.Count));
        var outcome = score >= definition.PassingScore ? AttemptOutcome.Passed : AttemptOutcome.Failed;
        var failedChapters = wrong.Select(definition.ChapterOf).Distinct().ToList();

        Raise(new AttemptSubmitted(Id, attempt.Id, score, outcome, failedChapters, now));

        if (outcome == AttemptOutcome.Passed)
        {
            Raise(new ExaminationPassed(Id, Session, Learner, Course, CourseVersion, attempt.Id, score, now));
        }
        else if (attempt.Number >= definition.MaxAttempts)
        {
            var allFailedChapters = attemptsSinceUnlock
                .SelectMany(earlier => FailedChaptersOf(earlier))
                .Concat(failedChapters)
                .Distinct()
                .ToList();
            Raise(new ExaminationExhausted(Id, Session, Learner, CourseVersion, allFailedChapters, now));
        }

        return Success(outcome);
    }

    // ── Apply ──

    private readonly Dictionary<AttemptId, IReadOnlyList<ChapterId>> failedChaptersByAttempt = [];
    private readonly List<TestAttempt> attemptsSinceUnlock = [];

    private IReadOnlyList<ChapterId> FailedChaptersOf(TestAttempt attempt) =>
        failedChaptersByAttempt.TryGetValue(attempt.Id, out var chapters) ? chapters : [];

    private void Apply(ExaminationOpened opened)
    {
        Id = opened.Examination;
        Session = opened.Session;
        Learner = opened.Learner;
        Course = opened.Course;
        CourseVersion = opened.CourseVersion;
        Status = ExaminationStatus.Unlocked;
    }

    private void Apply(ExaminationUnlocked _)
    {
        attemptsSinceUnlock.Clear();
        Status = ExaminationStatus.Unlocked;
    }

    private void Apply(AttemptStarted started)
    {
        var attempt = new TestAttempt(started.Attempt, started.Number, started.Questions);
        attempts.Add(attempt);
        attemptsSinceUnlock.Add(attempt);
        Status = ExaminationStatus.InAttempt;
    }

    private void Apply(QuestionAnswered answered) =>
        attempts.Single(attempt => attempt.Id == answered.Attempt).Answer(answered.Question, answered.Chosen);

    private void Apply(AttemptSubmitted submitted)
    {
        attempts.Single(attempt => attempt.Id == submitted.Attempt).Close(submitted.Score, submitted.Outcome);
        failedChaptersByAttempt[submitted.Attempt] = submitted.FailedChapters;
        Status = ExaminationStatus.Unlocked;
    }

    private void Apply(ExaminationPassed _) => Status = ExaminationStatus.Passed;

    private void Apply(ExaminationExhausted _) => Status = ExaminationStatus.Exhausted;
}

// ───────────────────────────── Domain events (stored forever) ─────────────────────────────
// ExaminationPassed → TestPassedV1, AttemptSubmitted (failed) → TestFailedV1, ExaminationExhausted → AttemptsExhaustedV1.

public sealed record ExaminationOpened(ExaminationId Examination, LearningSessionId Session, PersonId Learner, CourseId Course, CourseVersionId CourseVersion, Timestamp OpenedAt) : IDomainEvent;

public sealed record ExaminationUnlocked(ExaminationId Examination, Timestamp UnlockedAt) : IDomainEvent;

public sealed record AttemptStarted(ExaminationId Examination, AttemptId Attempt, AttemptNumber Number, IReadOnlyList<QuestionVersionId> Questions, Timestamp StartedAt) : IDomainEvent;

public sealed record QuestionAnswered(ExaminationId Examination, AttemptId Attempt, QuestionVersionId Question, IReadOnlyList<AnswerOptionId> Chosen, Timestamp AnsweredAt) : IDomainEvent;

public sealed record AttemptSubmitted(ExaminationId Examination, AttemptId Attempt, ScorePercent Score, AttemptOutcome Outcome, IReadOnlyList<ChapterId> FailedChapters, Timestamp SubmittedAt) : IDomainEvent;

public sealed record ExaminationPassed(ExaminationId Examination, LearningSessionId Session, PersonId Learner, CourseId Course, CourseVersionId CourseVersion, AttemptId Attempt, ScorePercent Score, Timestamp PassedAt) : IDomainEvent;

public sealed record ExaminationExhausted(ExaminationId Examination, LearningSessionId Session, PersonId Learner, CourseVersionId CourseVersion, IReadOnlyList<ChapterId> FailedChapters, Timestamp ExhaustedAt) : IDomainEvent;

// ───────────────────────────── Domain errors ─────────────────────────────

public sealed record ExaminationNotUnlocked(ExaminationId Examination, ExaminationStatus Status)
    : DomainError("assessment.examination-not-unlocked", "Der Test ist gerade nicht freigeschaltet.");

public sealed record NoOpenAttempt(ExaminationId Examination)
    : DomainError("assessment.no-open-attempt", "Es läuft gerade kein Testversuch.");

public sealed record QuestionNotInAttempt(ExaminationId Examination, QuestionVersionId Question)
    : DomainError("assessment.question-not-in-attempt", "Diese Frage gehört nicht zu diesem Versuch.");

public sealed record DefinitionOfAnotherVersion(ExaminationId Examination, CourseVersionId CourseVersion)
    : DomainError("assessment.definition-of-another-version", "Die Testdefinition passt nicht zur Schulungsversion.");
