using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

/// <summary>A stored examination is loaded by replaying its events: the replay must rebuild what raising them built.</summary>
public class ExaminationReplayTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly CourseVersionId Version = CourseVersionId.From(Guid.CreateVersion7());
    private static readonly AnswerOptionId Right = AnswerOptionId.From(Guid.CreateVersion7());
    private static readonly AnswerOptionId Wrong = AnswerOptionId.From(Guid.CreateVersion7());
    private static readonly QuestionVersionId Question = QuestionVersionId.From(Guid.CreateVersion7());

    private static readonly TestDefinition Definition = new(
        Version,
        [new DefinedQuestion(Question, ChapterId.From(Guid.CreateVersion7()), [Right, Wrong], [Right])],
        ScorePercent.From(100),
        AttemptNumber.From(3));

    // Two questions in two chapters, for replays that must keep which chapters failed.
    private static readonly ChapterId Hygiene = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Gloves = ChapterId.From(Guid.CreateVersion7());
    private static readonly QuestionVersionId HygieneQuestion = QuestionVersionId.From(Guid.CreateVersion7());
    private static readonly QuestionVersionId GlovesQuestion = QuestionVersionId.From(Guid.CreateVersion7());

    private static readonly TestDefinition TwoChapters = new(
        Version,
        [
            new DefinedQuestion(HygieneQuestion, Hygiene, [Right, Wrong], [Right]),
            new DefinedQuestion(GlovesQuestion, Gloves, [Right, Wrong], [Right]),
        ],
        ScorePercent.From(100),
        AttemptNumber.From(3));

    [Fact]
    public void Replaying_the_stored_events_rebuilds_the_same_examination()
    {
        var examination = Open();
        Attempt(examination, Wrong);
        Attempt(examination, Wrong);
        Attempt(examination, Right);

        var replayed = Replay(examination);

        replayed.Id.ShouldBe(examination.Id);
        replayed.Status.ShouldBe(ExaminationStatus.Passed);
        replayed.Status.ShouldBe(examination.Status);
        replayed.Session.ShouldBe(examination.Session);
        replayed.Learner.ShouldBe(examination.Learner);
        replayed.Course.ShouldBe(examination.Course);
        replayed.CourseVersion.ShouldBe(examination.CourseVersion);
        replayed.Attempts.Select(attempt => (attempt.Id, attempt.Number, attempt.Score, attempt.Outcome)).ShouldBe(
        [
            (examination.Attempts[0].Id, AttemptNumber.From(1), ScorePercent.From(0), AttemptOutcome.Failed),
            (examination.Attempts[1].Id, AttemptNumber.From(2), ScorePercent.From(0), AttemptOutcome.Failed),
            (examination.Attempts[2].Id, AttemptNumber.From(3), ScorePercent.From(100), AttemptOutcome.Passed),
        ]);
    }

    [Fact]
    public void Replaying_after_an_unlock_numbers_the_next_attempt_first()
    {
        var examination = Open();
        Attempt(examination, Wrong);
        Attempt(examination, Wrong);
        Attempt(examination, Wrong);
        examination.Unlock(Now);

        var replayed = Replay(examination);

        replayed.Status.ShouldBe(ExaminationStatus.Unlocked);
        replayed.StartAttempt(AttemptId.New(), [Question], Now).IsSuccess.ShouldBeTrue();
        replayed.Attempts[^1].Number.ShouldBe(AttemptNumber.First);
    }

    [Fact]
    public void Replaying_an_open_attempt_keeps_its_answers_for_the_submit()
    {
        var examination = Open();
        examination.StartAttempt(AttemptId.New(), [GlovesQuestion, HygieneQuestion], Now).IsSuccess.ShouldBeTrue();
        examination.Answer(HygieneQuestion, [Right], Now).IsSuccess.ShouldBeTrue();
        examination.Answer(GlovesQuestion, [Right], Now).IsSuccess.ShouldBeTrue();

        var replayed = Replay(examination);

        replayed.Status.ShouldBe(ExaminationStatus.InAttempt);
        replayed.Submit(TwoChapters, Now).Value.ShouldBe(AttemptOutcome.Passed);
        replayed.Status.ShouldBe(ExaminationStatus.Passed);
    }

    [Fact]
    public void Replaying_two_failed_attempts_keeps_their_chapters_for_the_exhaustion()
    {
        var examination = Open();
        Fail(examination, wrong: HygieneQuestion);
        Fail(examination, wrong: GlovesQuestion);

        var replayed = Replay(examination);
        Fail(replayed, wrong: HygieneQuestion);

        replayed.Status.ShouldBe(ExaminationStatus.Exhausted);
        ((IEventSourced)replayed).PendingEvents.OfType<ExaminationExhausted>().ShouldHaveSingleItem()
            .FailedChapters.ShouldBe([Hygiene, Gloves], ignoreOrder: true);
    }

    [Fact]
    public void Replaying_an_exhausted_examination_restores_when_it_was_exhausted()
    {
        var examination = Open();
        Attempt(examination, Wrong);
        Attempt(examination, Wrong);
        Attempt(examination, Wrong);

        var replayed = Replay(examination);

        replayed.ExhaustedAt.ShouldNotBeNull();
        replayed.ExhaustedAt.ShouldBe(examination.ExhaustedAt);
    }

    private static Examination Open() =>
        Examination.Open(
            ExaminationId.New(),
            LearningSessionId.From(Guid.CreateVersion7()),
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            Version,
            Now);

    private static void Attempt(Examination examination, AnswerOptionId chosen)
    {
        examination.StartAttempt(AttemptId.New(), [Question], Now).IsSuccess.ShouldBeTrue();
        examination.Answer(Question, [chosen], Now).IsSuccess.ShouldBeTrue();
        examination.Submit(Definition, Now).IsSuccess.ShouldBeTrue();
    }

    private static void Fail(Examination examination, QuestionVersionId wrong)
    {
        examination.StartAttempt(AttemptId.New(), [HygieneQuestion, GlovesQuestion], Now).IsSuccess.ShouldBeTrue();
        examination.Answer(HygieneQuestion, [wrong == HygieneQuestion ? Wrong : Right], Now).IsSuccess.ShouldBeTrue();
        examination.Answer(GlovesQuestion, [wrong == GlovesQuestion ? Wrong : Right], Now).IsSuccess.ShouldBeTrue();
        examination.Submit(TwoChapters, Now).Value.ShouldBe(AttemptOutcome.Failed);
    }

    private static Examination Replay(Examination examination)
    {
        var replayed = (Examination)Activator.CreateInstance(typeof(Examination), nonPublic: true)!;
        ((IEventSourced)replayed).Replay(((IEventSourced)examination).PendingEvents);
        return replayed;
    }
}
