using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>A membership id that names no active membership of the pharmacy, with the domain's own code for it.</summary>
public sealed record MembershipNotFound(Guid Id)
    : ApiError("organisation.no-active-membership", $"Es gibt keine aktive Mitgliedschaft {Id} in dieser Apotheke.", HttpStatusCode.NotFound);
