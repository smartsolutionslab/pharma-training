using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The course's latest version is retired, which leaves it without a version to assign.</summary>
public sealed record CourseRetired(Guid Id)
    : ApiError("compliance.course-retired", "The course is retired and cannot be assigned.", HttpStatusCode.Conflict);
