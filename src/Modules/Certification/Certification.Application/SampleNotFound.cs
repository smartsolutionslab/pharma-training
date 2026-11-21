using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

public sealed record SampleNotFound(Guid Id)
    : ApiError("certification.sample-not-found", $"There is no sample {Id}.", HttpStatusCode.NotFound);
