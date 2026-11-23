using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// A link no open invitation holds: unknown, used, revoked or renewed away. One answer for all, with the code and
/// message of the domain's <see cref="InvitationNotFound"/>, taken from it so the two cannot differ, and never the
/// pharmacy it may have belonged to.
/// </summary>
public sealed record InvitationLinkNotFound()
    : ApiError(Refusal.Code, Refusal.Message, HttpStatusCode.NotFound)
{
    /// <summary>The domain's refusal, read for its code and message only; the pharmacy it names never leaves.</summary>
    private static readonly InvitationNotFound Refusal = new(PharmacyId.New());
}
