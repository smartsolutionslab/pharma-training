using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A permanent failure that is not sealed: a subclass could be thrown for a transient cause and still dead-letter.</summary>
public class ProbeOpenPermanentFailure() : InvalidOperationException("A retry cannot succeed."), IPermanentMessageFailure;
