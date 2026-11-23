using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The course's latest version is retired, which leaves it without a version to assign.</summary>
public sealed record CourseRetired(Guid Id)
    : ApiError("compliance.course-retired", $"Die Schulung {Id} ist zurückgezogen und kann nicht zugewiesen werden.", HttpStatusCode.Conflict);
