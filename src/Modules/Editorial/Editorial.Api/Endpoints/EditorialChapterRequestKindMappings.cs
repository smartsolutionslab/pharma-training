using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>A request's kind as the module's; an undefined one is refused before it is mapped, so meeting one here is a bug.</summary>
internal static class EditorialChapterRequestKindMappings
{
    public static ChapterKind ToDomain(this EditorialChapterRequestKind kind) => kind switch
    {
        EditorialChapterRequestKind.Text => ChapterKind.Text,
        EditorialChapterRequestKind.Pdf => ChapterKind.Pdf,
        EditorialChapterRequestKind.Video => ChapterKind.Video,
        _ => throw new UnreachableException($"Undefined chapter kind {kind}."),
    };
}
