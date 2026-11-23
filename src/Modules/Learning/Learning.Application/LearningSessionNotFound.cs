using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record LearningSessionNotFound(Guid Id)
    : ApiError("learning.session-not-found", $"There is no learning session {Id} of yours.", HttpStatusCode.NotFound);
