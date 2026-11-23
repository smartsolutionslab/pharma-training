using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record IdentityIncomplete()
    : ApiError("learning.identity-incomplete", "The access token names no person (its subject is missing or no Guid).", HttpStatusCode.Forbidden);
