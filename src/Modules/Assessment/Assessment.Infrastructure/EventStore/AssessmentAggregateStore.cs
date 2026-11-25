using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using Marten;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.EventStore;

public sealed class AssessmentAggregateStore(IDocumentStore store, ILogger<AssessmentAggregateStore> logger)
    : MartenAggregateStore(store, logger), IAssessmentAggregateStore
{
    /// <summary>The module's event store lives in this schema, the one its DbContext uses.</summary>
    public const string Schema = "assessment";
}
