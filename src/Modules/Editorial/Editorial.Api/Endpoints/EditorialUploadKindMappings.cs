using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>An upload's kind as the kind its use case names; an undefined one is refused before it is mapped, so meeting one here is a bug.</summary>
internal static class EditorialUploadKindMappings
{
    public static EditorialChapterKind ToEditorialChapterKind(this EditorialUploadKind kind) => kind switch
    {
        EditorialUploadKind.Pdf => EditorialChapterKind.Pdf,
        EditorialUploadKind.Video => EditorialChapterKind.Video,
        _ => throw new UnreachableException($"Undefined upload kind {kind}."),
    };
}
