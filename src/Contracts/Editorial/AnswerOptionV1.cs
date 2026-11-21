namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>An answer option of a test question and whether it is correct, as <see cref="TestDefinitionPublishedV1"/> carries it.</summary>
public sealed record AnswerOptionV1(Guid AnswerOptionId, string Text, bool IsCorrect);
