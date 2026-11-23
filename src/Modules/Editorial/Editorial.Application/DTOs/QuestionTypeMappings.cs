using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// A question type under its own name in the endpoints and back, and in the integration events; an undefined one is a
/// bug.
/// </summary>
public static class QuestionTypeMappings
{
    public static EditorialQuestionType ToDto(this QuestionType type) => type switch
    {
        QuestionType.SingleChoice => EditorialQuestionType.SingleChoice,
        QuestionType.MultipleChoice => EditorialQuestionType.MultipleChoice,
        _ => throw new UnreachableException($"Undefined question type {type}."),
    };

    public static QuestionType ToDomain(this EditorialQuestionType type) => type switch
    {
        EditorialQuestionType.SingleChoice => QuestionType.SingleChoice,
        EditorialQuestionType.MultipleChoice => QuestionType.MultipleChoice,
        _ => throw new UnreachableException($"Undefined question type {type}."),
    };

    public static QuestionTypeV1 ToV1(this QuestionType type) => type switch
    {
        QuestionType.SingleChoice => QuestionTypeV1.SingleChoice,
        QuestionType.MultipleChoice => QuestionTypeV1.MultipleChoice,
        _ => throw new UnreachableException($"Undefined question type {type}."),
    };
}
