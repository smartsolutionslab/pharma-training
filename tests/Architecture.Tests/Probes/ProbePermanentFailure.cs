using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A failure that says retrying its message cannot succeed, as a module's would: sealed, with no inner exception.</summary>
public sealed class ProbePermanentFailure() : InvalidOperationException("A retry cannot succeed until someone changes something."), IPermanentMessageFailure;
