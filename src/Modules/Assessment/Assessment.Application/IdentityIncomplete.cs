using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

public sealed record IdentityIncomplete()
    : ApiError("assessment.identity-incomplete", "The access token names no person (its subject is missing or no Guid).", HttpStatusCode.Forbidden);
