using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

public class AssessmentIntegrationEventMappingsTests
{
    private static readonly Timestamp At = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly ChapterId Chapter = ChapterId.From(Guid.CreateVersion7());
    private static readonly QuestionVersionId Question = QuestionVersionId.From(Guid.CreateVersion7());
    private static readonly AnswerOptionId Right = AnswerOptionId.From(Guid.CreateVersion7());
    private static readonly AnswerOptionId Wrong = AnswerOptionId.From(Guid.CreateVersion7());

    [Fact]
    public void ExaminationPassed_maps_to_TestPassedV1_field_by_field()
    {
        var session = LearningSessionId.From(Guid.CreateVersion7());
        var passed = new ExaminationPassed(
            ExaminationId.For(session),
            session,
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            AttemptId.New(),
            ScorePercent.From(90),
            Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero)));

        var published = passed.ToIntegrationEvent();

        published.AttemptId.ShouldBe(passed.Attempt.Value);
        published.SessionId.ShouldBe(session.Value);
        published.SessionId.ShouldNotBe(passed.Examination.Value);
        published.PersonId.ShouldBe(passed.Learner.Value);
        published.CourseId.ShouldBe(passed.Course.Value);
        published.CourseVersionId.ShouldBe(passed.CourseVersion.Value);
        published.ScorePercent.ShouldBe(90);
        published.PassedAt.ShouldBe(passed.PassedAt.Value);
    }

    [Fact]
    public void A_failed_AttemptSubmitted_maps_to_TestFailedV1_with_the_examinations_session_and_the_attempts_number()
    {
        var examination = Opened();
        var attempt = Fail(examination);

        var failed = Pending<AttemptSubmitted>(examination).ToIntegrationEvent(examination);

        failed.AttemptId.ShouldBe(attempt.Value);
        failed.SessionId.ShouldBe(examination.Session.Value);
        failed.PersonId.ShouldBe(examination.Learner.Value);
        failed.CourseId.ShouldBe(examination.Course.Value);
        failed.CourseVersionId.ShouldBe(examination.CourseVersion.Value);
        failed.AttemptNumber.ShouldBe(1);
    }

    [Fact]
    public void A_passed_AttemptSubmitted_is_no_failure_to_announce()
    {
        var examination = Opened();
        examination.StartAttempt(AttemptId.New(), [Question], At).IsSuccess.ShouldBeTrue();
        examination.Answer(Question, [Right], At).IsSuccess.ShouldBeTrue();
        examination.Submit(DefinitionOf(examination), At).Value.ShouldBe(AttemptOutcome.Passed);

        Should.Throw<GuardException>(() => Pending<AttemptSubmitted>(examination).ToIntegrationEvent(examination));
    }

    [Fact]
    public void ExaminationExhausted_maps_to_AttemptsExhaustedV1_with_the_examinations_course()
    {
        var examination = Opened();
        Fail(examination);
        Fail(examination);
        Fail(examination);

        var exhausted = Pending<ExaminationExhausted>(examination).ToIntegrationEvent(examination);

        exhausted.SessionId.ShouldBe(examination.Session.Value);
        exhausted.PersonId.ShouldBe(examination.Learner.Value);
        exhausted.CourseId.ShouldBe(examination.Course.Value);
        exhausted.CourseVersionId.ShouldBe(examination.CourseVersion.Value);
        exhausted.FailedChapterIds.ShouldBe([Chapter.Value]);
    }

    private static Examination Opened()
    {
        var session = LearningSessionId.From(Guid.CreateVersion7());
        return Examination.Open(
            ExaminationId.For(session),
            session,
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            At);
    }

    private static TestDefinition DefinitionOf(Examination examination) =>
        new(examination.CourseVersion, [new DefinedQuestion(Question, Chapter, [Right, Wrong], [Right])], ScorePercent.From(100), AttemptNumber.From(3));

    private static AttemptId Fail(Examination examination)
    {
        var attempt = AttemptId.New();
        examination.StartAttempt(attempt, [Question], At).IsSuccess.ShouldBeTrue();
        examination.Answer(Question, [Wrong], At).IsSuccess.ShouldBeTrue();
        examination.Submit(DefinitionOf(examination), At).Value.ShouldBe(AttemptOutcome.Failed);
        return attempt;
    }

    private static TEvent Pending<TEvent>(Examination examination) =>
        ((IEventSourced)examination).PendingEvents.OfType<TEvent>().Last();
}
