using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>The Assessment module's aggregate store; implemented in its Infrastructure project by the event store.</summary>
public interface IAssessmentAggregateStore : IAggregateStore
{
    /// <summary>
    /// Saves like <see cref="IAggregateStore.SaveAsync"/>, but answers how it lost a race, with nothing stored: another
    /// append landed on the stream since the aggregate was loaded (<see cref="VersionMovedOn"/>), or PostgreSQL ended the
    /// save as a deadlock or a serialization failure (<see cref="SaveDeadlocked"/>) (ADR 0016).
    /// </summary>
    Task<Result<Unit, LostRace>> TrySaveAsync(IEventSourced aggregate, CancellationToken cancellationToken);
}
