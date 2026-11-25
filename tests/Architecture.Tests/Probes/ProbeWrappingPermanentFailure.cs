using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A permanent failure that can wrap another exception, which may be a dropped connection or a lost race.</summary>
public sealed class ProbeWrappingPermanentFailure(string message, Exception innerException)
    : InvalidOperationException(message, innerException), IPermanentMessageFailure;
