using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A chapter kind under its own name in the endpoints; an undefined one is a bug.</summary>
public static class ChapterKindMappings
{
    public static EditorialChapterKind ToDto(this ChapterKind kind) => kind switch
    {
        ChapterKind.Text => EditorialChapterKind.Text,
        ChapterKind.Pdf => EditorialChapterKind.Pdf,
        ChapterKind.Video => EditorialChapterKind.Video,
        _ => throw new UnreachableException($"Undefined chapter kind {kind}."),
    };
}
