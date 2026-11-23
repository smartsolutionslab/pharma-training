using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record ChapterHasNoMedia(Guid ChapterId)
    : ApiError("learning.chapter-has-no-media", $"Chapter {ChapterId} is a text chapter; it has no PDF or video.", HttpStatusCode.NotFound);
