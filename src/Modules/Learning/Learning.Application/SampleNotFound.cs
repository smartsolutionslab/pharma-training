using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record SampleNotFound(Guid Id)
    : ApiError("learning.sample-not-found", $"There is no sample {Id}.", HttpStatusCode.NotFound);
