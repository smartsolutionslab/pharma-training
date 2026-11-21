using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

public sealed record SampleNotFound(Guid Id)
    : ApiError("compliance.sample-not-found", $"There is no sample {Id}.", HttpStatusCode.NotFound);
