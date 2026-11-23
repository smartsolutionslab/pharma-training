namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An option of <see cref="ProbePaperQuestion"/> that carries its correctness.</summary>
public sealed record ProbePaperOption(Guid OptionId, string Text, bool IsCorrect);
