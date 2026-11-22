using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>The request carries no token, or one no link could hold: the caller's mistake, not a dead link.</summary>
public sealed record InvalidInvitationToken()
    : ApiError("organisation.invalid-invitation-token", "This is not an invitation link.", HttpStatusCode.BadRequest);
