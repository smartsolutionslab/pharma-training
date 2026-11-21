using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidCourseVersionStatus()
    : ApiError("editorial.invalid-status", "The status is one of Draft, InReview, Published, Superseded and Retired.", HttpStatusCode.BadRequest);
