using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

public sealed record SampleNotFound(Guid Id)
    : ApiError("compliance.sample-not-found", "There is no such sample.", HttpStatusCode.NotFound);
