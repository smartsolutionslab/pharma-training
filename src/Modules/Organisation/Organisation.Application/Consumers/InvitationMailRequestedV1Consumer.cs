using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>Red-first stub (T2): issues no link and sends no mail.</summary>
public sealed class InvitationMailRequestedV1Consumer(
    IPharmacyStore pharmacies,
    IOrganisationUnitOfWork unitOfWork,
    IMailSender mails,
    IFrontendLinks links,
    TimeProvider time,
    ILogger<InvitationMailRequestedV1Consumer> logger)
{
    public Task HandleAsync(InvitationMailRequestedV1 message, CancellationToken cancellationToken)
    {
        _ = (pharmacies, unitOfWork, mails, links, time, logger, message, cancellationToken);
        return Task.CompletedTask;
    }
}
