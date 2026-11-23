using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// A chapter kind under its own name in the endpoints and back, and in the integration events; an undefined one is a
/// bug.
/// </summary>
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

    public static ChapterKindV1 ToV1(this ChapterKind kind) => kind switch
    {
        ChapterKind.Text => ChapterKindV1.Text,
        ChapterKind.Pdf => ChapterKindV1.Pdf,
        ChapterKind.Video => ChapterKindV1.Video,
        _ => throw new UnreachableException($"Undefined chapter kind {kind}."),
    };
}
