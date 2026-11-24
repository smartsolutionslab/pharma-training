using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

public sealed record SampleNotFound(Guid Id)
    : ApiError("assessment.sample-not-found", "There is no such sample.", HttpStatusCode.NotFound);
