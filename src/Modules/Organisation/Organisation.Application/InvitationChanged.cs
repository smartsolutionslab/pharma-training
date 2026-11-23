using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// Another change to the invitation (a revoke, a renewal, another accept) was saved while this accept ran; nothing of
/// this one was saved. It names no pharmacy: the caller is not yet a member of it.
/// </summary>
public sealed record InvitationChanged()
    : ApiError("organisation.invitation-changed", "The invitation was changed while you accepted it. Open the link again.", HttpStatusCode.Conflict);
