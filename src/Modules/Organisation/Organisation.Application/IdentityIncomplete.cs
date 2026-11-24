using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record IdentityIncomplete()
    : ApiError("organisation.identity-incomplete", "The account names no person, no name or no valid e-mail address.", HttpStatusCode.Forbidden);
