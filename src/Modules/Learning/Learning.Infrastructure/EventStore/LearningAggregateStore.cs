using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using Marten;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.EventStore;

public sealed class LearningAggregateStore(IDocumentStore store, ILogger<LearningAggregateStore> logger)
    : MartenAggregateStore(store, logger), ILearningAggregateStore
{
    /// <summary>The module's event store lives in this schema, the one its DbContext uses.</summary>
    public const string Schema = "learning";
}
