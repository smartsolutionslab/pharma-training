namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Errors;

/// <summary>A domain error whose message is German, which ErrorMessagesAreEnglish names.</summary>
public sealed record ProbeGermanError() : DomainError("probe.german", "Die Probe fehlt.");
