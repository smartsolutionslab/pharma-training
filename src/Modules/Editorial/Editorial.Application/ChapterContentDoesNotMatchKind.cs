using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record ChapterContentDoesNotMatchKind()
    : ApiError(
        "editorial.chapter-content-does-not-match-kind",
        "A text chapter has text and no file or duration, a PDF a file and no text or duration, a video a file and a duration and no text.",
        HttpStatusCode.BadRequest);
