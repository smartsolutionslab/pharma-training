using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.EventStore;

/// <summary>
/// What a session's view does with each of the session's events, as Marten's conventions call them: the first event
/// creates the view, every later one changes it. A new event of the session gets its step here, so the projection
/// does not compile until it follows it.
/// </summary>
public interface ILearningSessionViewProjection
{
    LearningSessionView Create(SessionStarted started);

    void Apply(ChapterProgressRecorded recorded, LearningSessionView view);

    void Apply(ChapterCompleted completed, LearningSessionView view);

    void Apply(ContentCompleted completed, LearningSessionView view);

    void Apply(ChaptersReopened reopened, LearningSessionView view);

    void Apply(SessionFinished finished, LearningSessionView view);

    void Apply(ExhaustionNoted noted, LearningSessionView view);
}
