using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;

public sealed record CourseVersionNotFound(Guid Id)
    : ApiError("catalog.course-version-not-found", $"There is no course version {Id}.", HttpStatusCode.NotFound);
