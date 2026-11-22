using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A chapter kind under its own name in the endpoints and back; an undefined one is a bug.</summary>
public static class ChapterKindMappings
{
    public static EditorialChapterKind ToDto(this ChapterKind kind) => kind switch
    {
        ChapterKind.Text => EditorialChapterKind.Text,
        ChapterKind.Pdf => EditorialChapterKind.Pdf,
        ChapterKind.Video => EditorialChapterKind.Video,
        _ => throw new UnreachableException($"Undefined chapter kind {kind}."),
    };

    public static ChapterKind ToDomain(this EditorialChapterKind kind) => kind switch
    {
        EditorialChapterKind.Text => ChapterKind.Text,
        EditorialChapterKind.Pdf => ChapterKind.Pdf,
        EditorialChapterKind.Video => ChapterKind.Video,
        _ => throw new UnreachableException($"Undefined chapter kind {kind}."),
    };
}
