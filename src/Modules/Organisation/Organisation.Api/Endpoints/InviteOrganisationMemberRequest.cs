using System.Text.Json.Serialization;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>The body of <c>POST /organisation/pharmacies/{id}/invitations</c>: the address, and what the member will be.</summary>
public sealed record InviteOrganisationMemberRequest(
    string? Email,
    [property: JsonRequired] OrganisationJobFunction JobFunction,
    [property: JsonRequired] OrganisationMembershipRole Role);
