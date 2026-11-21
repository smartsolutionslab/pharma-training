namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>A question of a published test definition with its options, as <see cref="TestDefinitionPublishedV1"/> carries it.</summary>
public sealed record TestQuestionV1(Guid QuestionId, Guid ChapterId, QuestionTypeV1 Type, string Text, IReadOnlyList<AnswerOptionV1> Options);
