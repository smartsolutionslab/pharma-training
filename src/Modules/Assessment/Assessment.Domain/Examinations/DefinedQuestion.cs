namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// A question of a <see cref="TestDefinition"/> with its solution. For scoring only: never stored in a stream and
/// never put on a response.
/// </summary>
public sealed record DefinedQuestion(
    QuestionVersionId Question,
    ChapterId Chapter,
    IReadOnlyList<AnswerOptionId> Options,
    IReadOnlyList<AnswerOptionId> CorrectOptions);
