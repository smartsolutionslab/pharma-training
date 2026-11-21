using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using Marten;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.EventStore;

public sealed class LearningAggregateStore(IDocumentStore store)
    : MartenAggregateStore(store), ILearningAggregateStore
{
    /// <summary>The module's event store lives in this schema, the one its DbContext uses.</summary>
    public const string Schema = "learning";
}
