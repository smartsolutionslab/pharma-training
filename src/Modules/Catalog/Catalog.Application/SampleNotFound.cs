using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;

public sealed record SampleNotFound(Guid Id)
    : ApiError("catalog.sample-not-found", "There is no such sample.", HttpStatusCode.NotFound);
