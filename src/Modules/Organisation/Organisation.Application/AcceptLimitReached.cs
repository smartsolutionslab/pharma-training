using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The caller has accepted invitations too often in a short time (docs/adr/0004, docs/adr/0020): the limit only caps
/// how often one user looks up tokens, so it answers whatever the token, and the accept is not tried.
/// </summary>
public sealed record AcceptLimitReached()
    : ApiError("organisation.accept-limit-reached", "You have tried to accept invitations too often; try again later.", HttpStatusCode.TooManyRequests);
