using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidJobFunction()
    : ApiError("organisation.invalid-job-function", "The job function is missing or unknown.", HttpStatusCode.BadRequest);
