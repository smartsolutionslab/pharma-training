using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>The Assessment module's aggregate store; implemented in its Infrastructure project by the event store.</summary>
public interface IAssessmentAggregateStore : IAggregateStore
{
    /// <summary>Saves like <see cref="IAggregateStore.SaveAsync"/>; <see langword="false"/>, with nothing stored, when another append landed on the stream since the aggregate was loaded (ADR 0016).</summary>
    Task<bool> TrySaveAsync(IEventSourced aggregate, CancellationToken cancellationToken);
}
