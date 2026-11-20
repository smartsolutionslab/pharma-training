using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>The Assessment module's aggregate store; implemented in its Infrastructure project by the event store.</summary>
public interface IAssessmentAggregateStore : IAggregateStore;
