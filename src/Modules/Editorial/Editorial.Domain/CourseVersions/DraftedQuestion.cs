namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>What the LLM or an editor hands in for one question, before it becomes part of the version.</summary>
public sealed record DraftedQuestion(
    QuestionText Text,
    QuestionType Type,
    ChapterId Chapter,
    IReadOnlyList<(AnswerText Text, Correctness Correctness)> Options);
