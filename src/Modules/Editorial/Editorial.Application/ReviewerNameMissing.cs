using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// The token is incomplete, not the request wrong: an approval (#205) and a return (#220) need the caller's display name
/// to record.
/// </summary>
public sealed record ReviewerNameMissing()
    : ApiError("editorial.reviewer-name-missing", "The access token names no usable display name (claim `name`): it is missing, longer than 100 characters, or holds characters that cannot be shown.", HttpStatusCode.Forbidden);
