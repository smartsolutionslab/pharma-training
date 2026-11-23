using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The token is incomplete, not the request wrong: an approval needs the approver's display name to record (#205).</summary>
public sealed record ReviewerNameMissing()
    : ApiError("editorial.reviewer-name-missing", "The access token names no display name (claim `name`) to record with the approval.", HttpStatusCode.Forbidden);
