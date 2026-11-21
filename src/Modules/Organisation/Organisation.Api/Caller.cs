using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;

/// <summary>Who is calling, as the access token says: the Keycloak subject, the name they go by and their address.</summary>
public sealed record Caller(PersonId Person, DisplayName Name, EmailAddress Email);
