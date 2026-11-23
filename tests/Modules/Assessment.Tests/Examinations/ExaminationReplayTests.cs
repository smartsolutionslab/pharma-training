using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

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

    private static Examination Replay(Examination examination)
    {
        var replayed = (Examination)Activator.CreateInstance(typeof(Examination), nonPublic: true)!;
        ((IEventSourced)replayed).Replay(((IEventSourced)examination).PendingEvents);
        return replayed;
    }
}
