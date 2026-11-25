using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>The marker on a struct, which can never be an exception, so no consumer can throw it.</summary>
public readonly record struct ProbePermanentFailureStruct : IPermanentMessageFailure;
