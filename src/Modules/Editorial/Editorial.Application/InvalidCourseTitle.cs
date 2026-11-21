using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidCourseTitle()
    : ApiError("editorial.invalid-course-title", $"A course's title is 1 to {CourseTitle.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
