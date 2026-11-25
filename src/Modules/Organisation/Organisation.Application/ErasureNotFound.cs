using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record ErasureNotFound(Guid Id)
    : ApiError("organisation.erasure-not-found", "This erasure does not exist.", HttpStatusCode.NotFound);
