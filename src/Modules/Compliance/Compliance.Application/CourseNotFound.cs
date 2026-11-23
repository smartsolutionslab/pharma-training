using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>Compliance has received no version of the course.</summary>
public sealed record CourseNotFound(Guid Id)
    : ApiError("compliance.course-not-found", $"Es gibt keine Schulung {Id}.", HttpStatusCode.NotFound);
