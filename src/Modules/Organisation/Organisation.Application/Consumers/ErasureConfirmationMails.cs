using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>The erasure's confirmation mail, in German plain text; it names neither the person nor a pharmacy.</summary>
public static class ErasureConfirmationMails
{
    public static OutgoingMail Compose(EmailAddress address) => throw new NotImplementedException();
}
