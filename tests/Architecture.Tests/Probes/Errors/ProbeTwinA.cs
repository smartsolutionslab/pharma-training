namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Errors;

/// <summary>One of two errors sharing a code with different messages; see <see cref="ProbeTwinB"/>.</summary>
public sealed record ProbeTwinA() : DomainError("probe.twin", "The probe is missing.");
