using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>The account's address is not verified, so it proves nothing about who accepts.</summary>
public sealed record EmailNotVerified()
    : ApiError("organisation.email-not-verified", "Die E-Mail-Adresse des Kontos ist nicht bestätigt.", HttpStatusCode.Forbidden);
