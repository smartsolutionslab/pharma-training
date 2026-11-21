using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record IdentityIncomplete()
    : ApiError("editorial.identity-incomplete", "The access token names no subject that identifies a person.", HttpStatusCode.Forbidden);
