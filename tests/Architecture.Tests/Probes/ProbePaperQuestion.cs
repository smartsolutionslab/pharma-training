namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A question of <see cref="ProbeQuestionPaperResponse"/>.</summary>
public sealed record ProbePaperQuestion(Guid QuestionId, string Text, IReadOnlyList<ProbePaperOption> Options);
