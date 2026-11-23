using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record NoSessionOnVersion(Guid ChapterId)
    : ApiError("learning.no-session-on-version", $"You have no learning session on a course version that holds chapter {ChapterId}.", HttpStatusCode.Forbidden);
