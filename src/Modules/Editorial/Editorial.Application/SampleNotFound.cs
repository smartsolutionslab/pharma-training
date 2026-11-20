using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record SampleNotFound(Guid Id)
    : ApiError("editorial.sample-not-found", $"There is no sample {Id}.", HttpStatusCode.NotFound);
