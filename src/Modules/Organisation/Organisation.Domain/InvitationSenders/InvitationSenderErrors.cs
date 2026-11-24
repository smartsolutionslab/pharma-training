using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;

/// <summary>Everything the InvitationSender aggregate refuses, each with its own code; the Application maps them to an ApiError.</summary>
public static class InvitationSenderErrors
{
    /// <summary>Requesting an invitation mail beyond the sender's daily cap, before <paramref name="retryAt"/>.</summary>
    public static DomainError TooManyToday(PersonId sender, Timestamp retryAt) => new TooManyInvitationsSentToday(sender, retryAt);
}
