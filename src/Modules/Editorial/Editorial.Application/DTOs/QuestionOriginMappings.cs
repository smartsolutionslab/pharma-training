using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A question's origin under its own name in the endpoints; an undefined one is a bug.</summary>
public static class QuestionOriginMappings
{
    public static EditorialQuestionOrigin ToDto(this QuestionOrigin origin) => origin switch
    {
        QuestionOrigin.AiDraft => EditorialQuestionOrigin.AiDraft,
        QuestionOrigin.Copied => EditorialQuestionOrigin.Copied,
        QuestionOrigin.HandWritten => EditorialQuestionOrigin.HandWritten,
        _ => throw new UnreachableException($"Undefined origin {origin}."),
    };
}
