using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>An assignment names no course.</summary>
public sealed record CourseMissing()
    : ApiError("compliance.course-missing", "Die Schulung fehlt.", HttpStatusCode.BadRequest);
