using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record ChapterMediaNotReady(Guid ChapterId)
    : ApiError("learning.media-not-ready", $"The file of chapter {ChapterId} has not been copied into Learning yet; ask again shortly.", HttpStatusCode.Conflict);
