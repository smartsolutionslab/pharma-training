using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

/// <summary>Everything the InvitationRecipient aggregate refuses, each with its own code; the Application maps them to an ApiError.</summary>
public static class InvitationRecipientErrors
{
    /// <summary>Mailing the recipient within its cooldown, before <paramref name="retryAt"/>, refused at <paramref name="refusedAt"/>.</summary>
    public static DomainError MailedRecently(InvitationRecipientId recipient, Timestamp retryAt, Timestamp refusedAt) => new RecipientMailedRecently(recipient, retryAt, refusedAt);
}
