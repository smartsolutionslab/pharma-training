using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.EventStore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.LearningSessions;

/// <summary>A session's view as Marten builds it inline: the stream's first event through the projection's <c>Create</c>, every later one through its <c>Apply</c>.</summary>
internal static class LearningSessionViews
{
    public static LearningSessionView Project(IEnumerable<IDomainEvent> stream)
    {
        var projection = new LearningSessionViewProjection();
        LearningSessionView? view = null;
        foreach (var domainEvent in stream)
        {
            if (domainEvent is SessionStarted started)
            {
                view = projection.Create(started);
                continue;
            }

            var current = view ?? throw new InvalidOperationException($"{domainEvent.GetType().Name} came before the session started.");
            switch (domainEvent)
            {
                case ChapterProgressRecorded recorded:
                    projection.Apply(recorded, current);
                    break;
                case ChapterCompleted completed:
                    projection.Apply(completed, current);
                    break;
                case ContentCompleted contentCompleted:
                    projection.Apply(contentCompleted, current);
                    break;
                case ChaptersReopened reopened:
                    projection.Apply(reopened, current);
                    break;
                case SessionFinished finished:
                    projection.Apply(finished, current);
                    break;
                case ExhaustionNoted noted:
                    projection.Apply(noted, current);
                    break;
                default:
                    throw new InvalidOperationException($"The view knows no {domainEvent.GetType().Name}.");
            }
        }

        return view ?? throw new InvalidOperationException("The stream holds no event.");
    }
}
