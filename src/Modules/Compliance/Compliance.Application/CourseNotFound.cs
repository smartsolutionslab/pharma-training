using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>Compliance has received no version of the course.</summary>
public sealed record CourseNotFound(Guid Id)
    : ApiError("compliance.course-not-found", "There is no such course.", HttpStatusCode.NotFound);
