using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>Another change to the course version was saved after this one loaded it; nothing of this one was saved.</summary>
public sealed record CourseVersionChanged(Guid Id)
    : ApiError("editorial.course-version-changed", "The course version changed in the meantime; load it again and repeat the change.", HttpStatusCode.Conflict);
