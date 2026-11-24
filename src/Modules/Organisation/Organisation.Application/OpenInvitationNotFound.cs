using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// A membership id that names no open invitation of the pharmacy, with the code and message of the domain's
/// <see cref="NoOpenInvitation"/>, taken from it so the two cannot differ.
/// </summary>
public sealed record OpenInvitationNotFound(Guid Id)
    : ApiError(Refusal.Code, Refusal.Message, HttpStatusCode.NotFound)
{
    /// <summary>The domain's refusal, read for its code and message only.</summary>
    private static readonly NoOpenInvitation Refusal = new(PharmacyId.New(), MembershipId.New());
}
