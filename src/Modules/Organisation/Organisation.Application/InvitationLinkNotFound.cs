using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// A link no open invitation holds: unknown, used, revoked or renewed away. One answer for all, with the domain's code
/// and message, and never the pharmacy it may have belonged to.
/// </summary>
public sealed record InvitationLinkNotFound()
    : ApiError("organisation.invitation-not-found", "Der Einladungslink ist ungültig oder wurde schon benutzt.", HttpStatusCode.NotFound);
