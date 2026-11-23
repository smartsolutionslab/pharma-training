namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A question paper whose questions carry their answer keys; deliberately not named *Dto.</summary>
public sealed record ProbeLeakyPaper(Guid AttemptId, IReadOnlyList<ProbeLeakyQuestion> Questions);
