namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Errors;

/// <summary>A domain error two <see cref="ProbeFailures"/> methods map to different statuses.</summary>
public sealed record ProbeMappedTwice() : DomainError("probe.mapped-twice", "The probe is mapped twice.");
