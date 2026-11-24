namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Errors;

/// <summary>One of two errors sharing a code with different messages; see <see cref="ProbeTwinA"/>.</summary>
public sealed record ProbeTwinB() : DomainError("probe.twin", "There is no such probe.");
