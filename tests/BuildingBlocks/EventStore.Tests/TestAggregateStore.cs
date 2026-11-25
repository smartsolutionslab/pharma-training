using Marten;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore.Tests;

/// <summary>A module's store as the modules declare theirs: <see cref="MartenAggregateStore"/> over a document store.</summary>
public sealed class TestAggregateStore(IDocumentStore store, ILogger<TestAggregateStore> logger)
    : MartenAggregateStore(store, logger);
