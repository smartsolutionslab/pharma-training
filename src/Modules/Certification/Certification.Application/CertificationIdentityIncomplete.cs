using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

public sealed record CertificationIdentityIncomplete()
    : ApiError("certification.identity-incomplete", "The access token names no person (its subject is missing or no Guid).", HttpStatusCode.Forbidden);
