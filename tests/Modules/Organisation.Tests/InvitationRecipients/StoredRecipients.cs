using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.InvitationRecipients;

/// <summary>
/// Recipients as a store loads them, built the way the module builds them: an address not yet mailed, then a mail
/// counted at the instant given. The cooldown of one tick lets a mail be counted at any instant but the very last of
/// the calendar, so a test can store one ahead of its clock, as a hand-edited row or a clock that moved back does.
/// </summary>
internal static class StoredRecipients
{
    private static readonly RecipientCooldown OneTick = RecipientCooldown.From(TimeSpan.FromTicks(1));

    public static InvitationRecipient MailedAt(EmailAddress address, Timestamp at)
    {
        var recipient = InvitationRecipient.NotYetMailed(address);
        recipient.RequestMail(at, OneTick).IsSuccess.ShouldBeTrue($"no mail could be counted at {at.Value:O}");
        return recipient;
    }
}
