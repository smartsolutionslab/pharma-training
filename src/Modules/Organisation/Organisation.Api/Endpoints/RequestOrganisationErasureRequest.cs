namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>The body of <c>POST /organisation/erasures</c>: the person whose data is to be erased, by their id; never an address.</summary>
public sealed record RequestOrganisationErasureRequest(Guid PersonId);
