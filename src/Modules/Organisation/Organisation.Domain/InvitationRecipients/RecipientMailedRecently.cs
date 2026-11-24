using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

/// <summary>Mailing an address an invitation within its cooldown, whichever pharmacy mailed it; it may be mailed again from <paramref name="RetryAt"/>; refused at <paramref name="RefusedAt"/>, on the clock <paramref name="RetryAt"/> was computed on. It names no address.</summary>
public sealed record RecipientMailedRecently(InvitationRecipientId Recipient, Timestamp RetryAt, Timestamp RefusedAt)
    : DomainError("organisation.recipient-mailed-recently", "This address was sent an invitation mail a moment ago; invite it once the waiting time has passed.");
