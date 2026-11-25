using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Mails the erasure's confirmation to each of the person's addresses, then forgets their name and addresses in every
/// pharmacy, removes their invitation recipients and sender, records the confirmation and Organisation's step and
/// announces the erasure as <see cref="PersonErasureRequestedV1"/>, in one commit (#115). A confirmation that is handled
/// already is not mailed again.
/// </summary>
public sealed class ErasureConfirmationMailRequestedV1Consumer(
    IErasureStore erasures,
    IPharmacyStore pharmacies,
    IInvitationRecipientStore recipients,
    IInvitationSenderStore senders,
    IOrganisationUnitOfWork unitOfWork,
    IMailSender mails,
    TimeProvider time,
    ILogger<ErasureConfirmationMailRequestedV1Consumer> logger)
{
    private readonly IErasureStore erasures = erasures;
    private readonly IPharmacyStore pharmacies = pharmacies;
    private readonly IInvitationRecipientStore recipients = recipients;
    private readonly IInvitationSenderStore senders = senders;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly IMailSender mails = mails;
    private readonly TimeProvider time = time;
    private readonly ILogger<ErasureConfirmationMailRequestedV1Consumer> logger = logger;

    public Task HandleAsync(ErasureConfirmationMailRequestedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
