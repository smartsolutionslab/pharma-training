using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

/// <summary>
/// An examination remembers when it was last exhausted: a completion Learning published after that instant unlocks it,
/// an older one does not (docs/adr/0012).
/// </summary>
public class ExaminationExhaustionTests
{
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Timestamp T0 = At(0);
    private static readonly Timestamp T3 = At(3);
    private static readonly Timestamp T4 = At(4);
    private static readonly Timestamp T7 = At(7);
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
    public void A_new_examination_was_never_exhausted()
    {
        Open().ExhaustedAt.ShouldBeNull();
    }

    [Fact]
    public void An_exhausted_examination_knows_when_it_was_exhausted()
    {
        var examination = Open();

        Exhaust(examination, from: 1);

        examination.Status.ShouldBe(ExaminationStatus.Exhausted);
        examination.ExhaustedAt.ShouldBe(T3);
        examination.ExhaustedAt.ShouldBe(Pending(examination).OfType<ExaminationExhausted>().Single().ExhaustedAt);
    }

    [Fact]
    public void An_unlock_keeps_the_instant_of_the_last_exhaustion()
    {
        var examination = Open();
        Exhaust(examination, from: 1);

        examination.Unlock(T4);

        examination.Status.ShouldBe(ExaminationStatus.Unlocked);
        examination.ExhaustedAt.ShouldBe(T3);
    }

    [Fact]
    public void A_second_exhaustion_replaces_the_instant_of_the_first()
    {
        var examination = Open();
        Exhaust(examination, from: 1);
        examination.Unlock(T4);

        Exhaust(examination, from: 5);

        examination.Status.ShouldBe(ExaminationStatus.Exhausted);
        examination.ExhaustedAt.ShouldBe(T7);
    }

    private static Examination Open() =>
        Examination.Open(
            ExaminationId.New(),
            LearningSessionId.From(Guid.CreateVersion7()),
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            Version,
            T0);

    /// <summary>Three failed attempts, submitted at hours <paramref name="from"/>, +1 and +2 after the start.</summary>
    private static void Exhaust(Examination examination, int from)
    {
        for (var hour = from; hour < from + 3; hour++)
        {
            var now = At(hour);
            examination.StartAttempt(AttemptId.New(), [Question], now).IsSuccess.ShouldBeTrue();
            examination.Answer(Question, [Wrong], now).IsSuccess.ShouldBeTrue();
            examination.Submit(Definition, now).Value.ShouldBe(AttemptOutcome.Failed);
        }
    }

    private static Timestamp At(int hour) => Timestamp.From(Start.AddHours(hour));

    private static IReadOnlyList<IDomainEvent> Pending(Examination examination) => ((IEventSourced)examination).PendingEvents;
}
