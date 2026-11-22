using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A question type under its own name in the endpoints and back; an undefined one is a bug.</summary>
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
}
