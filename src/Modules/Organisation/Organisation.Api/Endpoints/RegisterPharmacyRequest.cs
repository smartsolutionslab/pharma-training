using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>The body of <c>POST /organisation/pharmacies</c>: the pharmacy's name and what the founder does there.</summary>
public sealed record RegisterPharmacyRequest(string? Name, OrganisationJobFunction? JobFunction);
