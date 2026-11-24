using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record CourseVersionNotFound(Guid Id)
    : ApiError("editorial.course-version-not-found", "There is no such course version.", HttpStatusCode.NotFound);
