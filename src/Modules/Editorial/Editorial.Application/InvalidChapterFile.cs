using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidChapterFile()
    : ApiError(
        "editorial.invalid-chapter-file",
        "A PDF or video chapter's file is the blob name an upload for this version and kind answered, never a URL, or the file the chapter already has.",
        HttpStatusCode.BadRequest);
