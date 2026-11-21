using System.Text.Json.Serialization;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>The body of <c>PATCH /organisation/pharmacies/{id}/members/{membershipId}</c>: both are set together.</summary>
public sealed record ChangeOrganisationMembershipRequest(
    [property: JsonRequired] OrganisationJobFunction JobFunction,
    [property: JsonRequired] OrganisationMembershipRole Role);
