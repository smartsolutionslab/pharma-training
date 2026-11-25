using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// What Organisation reads of a person's Keycloak account: its address, only where Keycloak verified it and it is a valid
/// address (docs/adr/0014), whether it is a client's service account, and whether it holds one of the
/// <see cref="StaffRoles"/> or any role of the <c>realm-management</c> client (a delegated realm administrator). Neither a service account nor a staff account is ever deleted by an erasure (docs/adr/0040).
/// </summary>
public sealed record Account(Option<EmailAddress> VerifiedAddress, bool IsServiceAccount, bool HoldsStaffRole);
