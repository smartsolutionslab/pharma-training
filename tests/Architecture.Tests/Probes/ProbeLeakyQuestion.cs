namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A question that carries its answer key beside its text.</summary>
public sealed record ProbeLeakyQuestion(Guid QuestionId, string Text, IReadOnlyList<Guid> AnswerKey);
