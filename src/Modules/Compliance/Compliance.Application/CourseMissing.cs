using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment names no course.</summary>
public sealed record CourseMissing()
    : ApiError("compliance.course-missing", "The course is missing.", HttpStatusCode.BadRequest);
