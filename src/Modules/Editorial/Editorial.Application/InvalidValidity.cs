using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidValidity()
    : ApiError("editorial.invalid-validity", $"A certificate is valid for {RecurrenceInterval.MinimumMonths} to {RecurrenceInterval.MaximumMonths} months.", HttpStatusCode.BadRequest);
