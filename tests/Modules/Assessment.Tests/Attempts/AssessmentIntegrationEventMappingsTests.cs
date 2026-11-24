using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

public class AssessmentIntegrationEventMappingsTests
{
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
}
