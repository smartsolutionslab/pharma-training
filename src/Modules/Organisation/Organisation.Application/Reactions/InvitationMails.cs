using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Reactions;

/// <summary>The invitation mail, in German. Stub for #9's red tests (T8): an empty mail.</summary>
public static class InvitationMails
{
    public static OutgoingMail Compose(MemberInvited invited, Uri link) => new(string.Empty, string.Empty, string.Empty);
}
