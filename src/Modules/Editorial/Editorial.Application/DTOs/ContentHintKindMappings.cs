using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A content review hint's kind under its own name in the endpoints; an undefined one is a bug.</summary>
public static class ContentHintKindMappings
{
    public static EditorialContentHintKind ToDto(this ContentHintKind kind) => kind switch
    {
        ContentHintKind.Gap => EditorialContentHintKind.Gap,
        ContentHintKind.Contradiction => EditorialContentHintKind.Contradiction,
        ContentHintKind.Readability => EditorialContentHintKind.Readability,
        _ => throw new UnreachableException($"Undefined hint kind {kind}."),
    };
}
