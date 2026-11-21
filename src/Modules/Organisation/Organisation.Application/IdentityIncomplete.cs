using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record IdentityIncomplete()
    : ApiError("organisation.identity-incomplete", "Das Konto liefert keine Person, keinen Namen oder keine gültige E-Mail-Adresse.", HttpStatusCode.Forbidden);
