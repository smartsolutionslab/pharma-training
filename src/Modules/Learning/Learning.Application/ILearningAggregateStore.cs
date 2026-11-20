using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>The Learning module's aggregate store; implemented in its Infrastructure project by the event store.</summary>
public interface ILearningAggregateStore : IAggregateStore;
