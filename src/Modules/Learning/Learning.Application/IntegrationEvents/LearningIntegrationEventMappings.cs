using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.IntegrationEvents;

/// <summary>What Learning announces to other modules, made from the session's own events; publishing fills the metadata.</summary>
public static class LearningIntegrationEventMappings
{
    public static LearningSessionStartedV1 ToIntegrationEvent(this SessionStarted started)
    {
        Ensure.That(started).IsNotNull();
        return new LearningSessionStartedV1(started.Session.Value, started.Learner.Value, started.Course.Value, started.CourseVersion.Value);
    }

    public static ContentCompletedV1 ToIntegrationEvent(this ContentCompleted completed)
    {
        Ensure.That(completed).IsNotNull();
        return new ContentCompletedV1(completed.Session.Value, completed.Learner.Value, completed.Course.Value, completed.CourseVersion.Value);
    }
}
