using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record SampleNotFound(Guid Id)
    : ApiError("organisation.sample-not-found", $"There is no sample {Id}.", HttpStatusCode.NotFound);
