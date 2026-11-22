using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>A membership id that names no open invitation of the pharmacy, with the domain's own code for it.</summary>
public sealed record OpenInvitationNotFound(Guid Id)
    : ApiError("organisation.no-open-invitation", $"Es gibt keine offene Einladung {Id} in dieser Apotheke.", HttpStatusCode.NotFound);
