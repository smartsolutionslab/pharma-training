using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record LearningSessionNotFound(Guid Id)
    : ApiError("learning.session-not-found", "You have no such learning session.", HttpStatusCode.NotFound);
