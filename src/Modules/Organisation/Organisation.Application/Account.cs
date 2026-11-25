using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// What Organisation reads of a person's Keycloak account: its address, only where Keycloak verified it and it is a valid
/// address (docs/adr/0014), and whether it is a client's service account, which is never deleted.
/// </summary>
public sealed record Account(Option<EmailAddress> VerifiedAddress, bool IsServiceAccount);
