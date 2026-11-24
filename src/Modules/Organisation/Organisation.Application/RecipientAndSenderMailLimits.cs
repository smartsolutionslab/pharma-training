using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// How often an address may be mailed an invitation and how many a sender may request, across every pharmacy
/// (docs/adr/0004): an address at most once per <see cref="RecipientCooldown"/>, a sender at most
/// <see cref="MailsPerSenderPerDay"/> per UTC day. The host's configuration sets them; <see cref="Default"/> is 10
/// minutes and 100 mails.
/// </summary>
public sealed record RecipientAndSenderMailLimits(RecipientCooldown RecipientCooldown, DailyMailCap MailsPerSenderPerDay)
{
    public const int DefaultRecipientCooldownMinutes = 10;

    public const int DefaultMailsPerSenderPerDay = 100;

    public static RecipientAndSenderMailLimits Default { get; } = new(
        RecipientCooldown.From(TimeSpan.FromMinutes(DefaultRecipientCooldownMinutes)),
        DailyMailCap.From(DefaultMailsPerSenderPerDay));
}
