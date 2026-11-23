using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidReturnReason()
    : ApiError("editorial.invalid-return-reason", ReturnReason.Message, HttpStatusCode.BadRequest);
