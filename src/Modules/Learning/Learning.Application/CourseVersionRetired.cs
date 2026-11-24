using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record CourseVersionRetired(Guid Id)
    : ApiError("learning.course-version-retired", $"Course version {Id} is retired; a new learning session cannot start on it. A session already started on it can still be resumed.", HttpStatusCode.Conflict);
