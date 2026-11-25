using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>The marker on something that is no exception, which no consumer can throw.</summary>
public sealed record ProbePermanentFailureMarker : IPermanentMessageFailure;
