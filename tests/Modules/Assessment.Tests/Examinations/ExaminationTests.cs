// Assessment — behaviour of the event-sourced Examination aggregate.

using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

public class ExaminationTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly CourseVersionId Version = CourseVersionId.From(Guid.CreateVersion7());
    private static readonly ChapterId Hygiene = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Gloves = ChapterId.From(Guid.CreateVersion7());

    // Q1 single choice (Hygiene): A right. Q2 multiple choice (Gloves): B and C right.
    private static readonly AnswerOptionId A = NewOption(), B = NewOption(), C = NewOption(), D = NewOption();
    private static readonly QuestionVersionId Q1 = QuestionVersionId.From(Guid.CreateVersion7());
    private static readonly QuestionVersionId Q2 = QuestionVersionId.From(Guid.CreateVersion7());

    private static readonly TestDefinition Definition = new(
        Version,
        [
            new DefinedQuestion(Q1, Hygiene, [A, D], [A]),
            new DefinedQuestion(Q2, Gloves, [B, C, D], [B, C]),
        ],
        ScorePercent.From(100),
        AttemptNumber.From(3));

    [Fact]
    public void A_new_examination_is_unlocked()
    {
        Open().Status.ShouldBe(ExaminationStatus.Unlocked);
    }

    [Fact]
    public void Only_one_attempt_runs_at_a_time()
    {
        var examination = Open();
        StartAttempt(examination);

        examination.StartAttempt(AttemptId.New(), [Q1, Q2], Now).Error.ShouldBeOfType<ExaminationNotUnlocked>();
    }

    [Fact]
    public void Exactly_the_right_options_pass()
    {
        var examination = Open();
        StartAttempt(examination);
        examination.Answer(Q1, [A], Now);
        examination.Answer(Q2, [C, B], Now);

        examination.Submit(Definition, Now).Value.ShouldBe(AttemptOutcome.Passed);

        examination.Status.ShouldBe(ExaminationStatus.Passed);
        Pending(examination).OfType<ExaminationPassed>().ShouldHaveSingleItem().Score.ShouldBe(ScorePercent.From(100));
    }

    [Fact]
    public void Two_of_three_right_options_earn_no_partial_credit()
    {
        var examination = Open();
        StartAttempt(examination);
        examination.Answer(Q1, [A], Now);
        examination.Answer(Q2, [B], Now);

        examination.Submit(Definition, Now).Value.ShouldBe(AttemptOutcome.Failed);

        Pending(examination).OfType<AttemptSubmitted>().Single().Score.ShouldBe(ScorePercent.From(50));
    }

    [Fact]
    public void An_unanswered_question_is_wrong()
    {
        var examination = Open();
        StartAttempt(examination);
        examination.Answer(Q1, [A], Now);

        examination.Submit(Definition, Now).Value.ShouldBe(AttemptOutcome.Failed);
        Pending(examination).OfType<AttemptSubmitted>().Single().FailedChapters.ShouldBe([Gloves]);
    }

    [Fact]
    public void A_question_from_outside_the_attempt_is_refused()
    {
        var examination = Open();
        examination.StartAttempt(AttemptId.New(), [Q1], Now);

        examination.Answer(Q2, [B], Now).Error.ShouldBeOfType<QuestionNotInAttempt>();
    }

    [Fact]
    public void The_third_failed_attempt_locks_the_examination_and_names_every_failed_chapter()
    {
        var examination = Open();

        Fail(examination, wrong: Q1);
        Fail(examination, wrong: Q2);
        Fail(examination, wrong: Q1);

        examination.Status.ShouldBe(ExaminationStatus.Exhausted);
        Pending(examination).OfType<ExaminationExhausted>().ShouldHaveSingleItem()
            .FailedChapters.ShouldBe([Hygiene, Gloves], ignoreOrder: true);
    }

    [Fact]
    public void A_locked_examination_takes_no_attempt_until_unlocked_and_then_three_more()
    {
        var examination = Open();
        Fail(examination, wrong: Q1);
        Fail(examination, wrong: Q1);
        Fail(examination, wrong: Q1);

        examination.StartAttempt(AttemptId.New(), [Q1, Q2], Now).Error.ShouldBeOfType<ExaminationNotUnlocked>();

        examination.Unlock(Now);
        StartAttempt(examination);
        examination.Attempts[^1].Number.ShouldBe(AttemptNumber.First);
    }

    [Fact]
    public void A_passed_examination_takes_no_further_attempt()
    {
        var examination = Open();
        StartAttempt(examination);
        examination.Answer(Q1, [A], Now);
        examination.Answer(Q2, [B, C], Now);
        examination.Submit(Definition, Now);

        examination.StartAttempt(AttemptId.New(), [Q1, Q2], Now).Error.ShouldBeOfType<ExaminationNotUnlocked>();
    }

    [Fact]
    public void The_definition_of_another_version_is_refused()
    {
        var examination = Open();
        StartAttempt(examination);
        var other = Definition with { CourseVersion = CourseVersionId.From(Guid.CreateVersion7()) };

        examination.Submit(other, Now).Error.ShouldBeOfType<DefinitionOfAnotherVersion>();
    }

    private static Examination Open() =>
        Examination.Open(
            ExaminationId.New(),
            LearningSessionId.From(Guid.CreateVersion7()),
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            Version,
            Now);

    private static void StartAttempt(Examination examination) =>
        examination.StartAttempt(AttemptId.New(), [Q2, Q1], Now).IsSuccess.ShouldBeTrue();

    private static void Fail(Examination examination, QuestionVersionId wrong)
    {
        StartAttempt(examination);
        examination.Answer(Q1, wrong == Q1 ? [D] : [A], Now);
        examination.Answer(Q2, wrong == Q2 ? [D] : [B, C], Now);
        examination.Submit(Definition, Now).Value.ShouldBe(AttemptOutcome.Failed);
    }

    private static IReadOnlyList<IDomainEvent> Pending(Examination examination) => ((IEventSourced)examination).PendingEvents;

    private static AnswerOptionId NewOption() => AnswerOptionId.From(Guid.CreateVersion7());
}
