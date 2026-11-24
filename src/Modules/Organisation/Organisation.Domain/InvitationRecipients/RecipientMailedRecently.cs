using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

/// <summary>Mailing an address an invitation within its cooldown, whichever pharmacy mailed it; it may be mailed again from <paramref name="RetryAt"/>. It names no address.</summary>
public sealed record RecipientMailedRecently(InvitationRecipientId Recipient, Timestamp RetryAt)
    : DomainError("organisation.recipient-mailed-recently", "This address was sent an invitation mail a moment ago; invite it once the waiting time has passed.");
