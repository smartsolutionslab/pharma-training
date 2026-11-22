namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>The body of <c>POST /organisation/invitations/accept</c>: the token from the link's fragment, nothing else.</summary>
public sealed record AcceptOrganisationInvitationRequest(string? Token);
