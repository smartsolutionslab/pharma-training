using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record CourseVersionNotFound(Guid Id)
    : ApiError("learning.course-version-not-found", $"Learning holds no course version {Id}; a version just published may not have reached it yet.", HttpStatusCode.NotFound);
