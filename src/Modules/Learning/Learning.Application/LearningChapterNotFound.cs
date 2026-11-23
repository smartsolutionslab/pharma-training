using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record LearningChapterNotFound(Guid Id)
    : ApiError("learning.chapter-not-found", $"Learning holds no chapter {Id}.", HttpStatusCode.NotFound);
