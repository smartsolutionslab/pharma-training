using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;

/// <summary>What Assessment announces of its examinations. Publishing fills each event's metadata.</summary>
public static class AssessmentIntegrationEventMappings
{
    /// <summary>A pass, for Learning's session: <c>SessionId</c> is the learning session that opened the examination, never the examination's own id.</summary>
    public static TestPassedV1 ToIntegrationEvent(this ExaminationPassed passed)
    {
        Ensure.That(passed).IsNotNull();

        return new TestPassedV1(
            passed.Attempt.Value,
            passed.Session.Value,
            passed.Learner.Value,
            passed.Course.Value,
            passed.CourseVersion.Value,
            passed.Score.Value,
            passed.PassedAt.Value);
    }
}
