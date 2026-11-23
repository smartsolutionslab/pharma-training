using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// The examination of one learning session, not the single attempt: "at most three attempts" spans every attempt, so
/// they share one consistency boundary. Scoring is on the server, without partial credit.
/// </summary>
public sealed class Examination : EventSourcedAggregate<ExaminationId>
{
    private readonly List<TestAttempt> attempts = [];
    private readonly Dictionary<AttemptId, IReadOnlyList<ChapterId>> failedChaptersByAttempt = [];
    private readonly List<TestAttempt> attemptsSinceUnlock = [];

    // Only loading creates an empty examination, which its first event fills.
    private Examination()
    {
    }

    public LearningSessionId Session { get; private set; } = null!;

    public PersonId Learner { get; private set; } = null!;

    public CourseId Course { get; private set; } = null!;

    public CourseVersionId CourseVersion { get; private set; } = null!;

    public ExaminationStatus Status { get; private set; }

    /// <summary>When the examination was last exhausted; null until it first is. A completion published after it unlocks (docs/adr/0012).</summary>
    public Timestamp? ExhaustedAt { get; private set; }

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
        Ensure.That(id).IsNotNull();
        Ensure.That(session).IsNotNull();
        Ensure.That(learner).IsNotNull();
        Ensure.That(course).IsNotNull();
        Ensure.That(courseVersion).IsNotNull();
        Ensure.That(now).IsNotNull();

        var examination = new Examination();
        examination.Raise(new ExaminationOpened(id, session, learner, course, courseVersion, now));
        return examination;
    }

    /// <summary>
    /// ContentCompleted again after the reopened chapters were worked through: three new attempts. Only an exhausted
    /// examination unlocks; for any other this changes nothing.
    /// </summary>
    public void Unlock(Timestamp now)
    {
        Ensure.That(now).IsNotNull();

        if (Status == ExaminationStatus.Exhausted)
        {
            Raise(new ExaminationUnlocked(Id, now));
        }
    }

    /// <param name="attempt">The new attempt's id.</param>
    /// <param name="shuffledQuestions">The definition's questions in this attempt's random order (shuffled by the Application), at least one.</param>
    /// <param name="now">When the attempt starts.</param>
    public Result<AttemptId, DomainError> StartAttempt(AttemptId attempt, IReadOnlyList<QuestionVersionId> shuffledQuestions, Timestamp now)
    {
        Ensure.That(attempt).IsNotNull();
        Ensure.That(shuffledQuestions).IsNotNull();
        Ensure.That(shuffledQuestions.Count, nameof(shuffledQuestions)).AtLeast(1);
        Ensure.That(now).IsNotNull();

        if (Status != ExaminationStatus.Unlocked)
        {
            return Failure(ExaminationErrors.NotUnlocked(Id, Status));
        }

        var number = attemptsSinceUnlock.Count == 0 ? AttemptNumber.First : attemptsSinceUnlock[^1].Number.Next();
        Raise(new AttemptStarted(Id, attempt, number, [.. shuffledQuestions], now));
        return Success(attempt);
    }

    public Result<Unit, DomainError> Answer(QuestionVersionId question, IReadOnlyList<AnswerOptionId> chosen, Timestamp now)
    {
        Ensure.That(question).IsNotNull();
        Ensure.That(chosen).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (OpenAttempt is not { } attempt)
        {
            return Failure(ExaminationErrors.NoOpenAttempt(Id));
        }

        if (!attempt.Questions.Contains(question))
        {
            return Failure(ExaminationErrors.QuestionNotInAttempt(Id, question));
        }

        Raise(new QuestionAnswered(Id, attempt.Id, question, [.. chosen], now));
        return Success(Unit.Value);
    }

    /// <summary>Scores the open attempt on the server against the version's definition. Unanswered questions are wrong.</summary>
    public Result<AttemptOutcome, DomainError> Submit(TestDefinition definition, Timestamp now)
    {
        Ensure.That(definition).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (OpenAttempt is not { } attempt)
        {
            return Failure(ExaminationErrors.NoOpenAttempt(Id));
        }

        if (definition.CourseVersion != CourseVersion)
        {
            return Failure(ExaminationErrors.DefinitionOfAnotherVersion(Id, definition.CourseVersion));
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
                .SelectMany(FailedChaptersOf)
                .Concat(failedChapters)
                .Distinct()
                .ToList();
            Raise(new ExaminationExhausted(Id, Session, Learner, CourseVersion, allFailedChapters, now));
        }

        return Success(outcome);
    }

    private IReadOnlyList<ChapterId> FailedChaptersOf(TestAttempt attempt) =>
        failedChaptersByAttempt.TryGetValue(attempt.Id, out var chapters) ? chapters : [];

    // Apply: the only place state changes; replaying the stream rebuilds the same examination.
    private void Apply(ExaminationOpened opened)
    {
        Id = opened.Examination;
        Session = opened.Session;
        Learner = opened.Learner;
        Course = opened.Course;
        CourseVersion = opened.CourseVersion;
        Status = ExaminationStatus.Unlocked;
    }

    private void Apply(ExaminationUnlocked unlocked)
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

    private void Apply(ExaminationPassed passed) => Status = ExaminationStatus.Passed;

    private void Apply(ExaminationExhausted exhausted)
    {
        Status = ExaminationStatus.Exhausted;
        ExhaustedAt = exhausted.ExhaustedAt;
    }
}
