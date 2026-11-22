namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// A question at its place in the version, counted from 1, with its options in their order, where it came from, and
/// who signed it off when (story E7); <c>ReviewedBy</c> is the approver's person id, and both are null while unreviewed.
/// </summary>
public sealed record EditorialQuestionDto(
    Guid Id,
    int Position,
    Guid ChapterId,
    string Text,
    EditorialQuestionType Type,
    IReadOnlyList<EditorialAnswerOptionDto> Options,
    EditorialQuestionProvenance Provenance,
    EditorialQuestionReview Review,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt);
