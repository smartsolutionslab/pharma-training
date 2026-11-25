using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Mails the erasure's confirmation to each of the person's addresses, then forgets their name and addresses in every
/// pharmacy, removes their invitation recipients and sender, records the confirmation and Organisation's step and
/// announces the erasure as <see cref="PersonErasureRequestedV1"/>, in one commit (#115). A confirmation that is handled
/// already is not mailed again.
/// <para>
/// It is idempotent by the erasure's state, not by <c>Metadata.EventIdentifier</c>: a send that failed must be sent
/// again, and the handled confirmation is the key. A send that throws forgets nothing and rethrows, so Wolverine retries
/// and finally dead-letters the message, which holds ids only. When a send returned and a later one or the commit failed,
/// the retry mails the addresses again (the gap ADR 0004 accepts for invitations). A membership that is invited or active
/// again by now (opened after the request) is not forgotten and is logged. A lost race throws, and the retry reloads.
/// </para>
/// </summary>
public sealed partial class ErasureConfirmationMailRequestedV1Consumer(
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

    public async Task HandleAsync(ErasureConfirmationMailRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.ErasureId, nameof(message.ErasureId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();

        var found = await erasures.LoadAsync(ErasureId.From(message.ErasureId), cancellationToken);
        if (!found.HasValue)
        {
            ErasureGone(logger, message.ErasureId);
            return;
        }

        var erasure = found.Value;
        if (erasure.Confirmation != ErasureConfirmation.Pending)
        {
            AlreadyHandled(logger, message.ErasureId);
            return;
        }

        var person = PersonId.From(message.PersonId);
        var addresses = await pharmacies.AddressesOfAsync(person, cancellationToken);

        // Every mail is out before anything is forgotten: once the address is gone, nothing could mail it.
        foreach (var address in addresses)
        {
            await mails.SendAsync(ErasureConfirmationMails.Compose(address), cancellationToken);
        }

        var touched = await pharmacies.LoadTouchingPersonAsync(person, addresses, cancellationToken);
        foreach (var pharmacy in touched)
        {
            foreach (var skipped in pharmacy.ForgetPerson(person, addresses))
            {
                StillOpen(logger, message.ErasureId, skipped.Value);
            }
        }

        foreach (var address in addresses)
        {
            await recipients.RemoveAsync(InvitationRecipientId.For(address), cancellationToken);
        }

        await senders.RemoveAsync(person, cancellationToken);

        var now = Timestamp.From(time.GetUtcNow());
        erasure.ConfirmationHandled(Confirmation(addresses), now);
        erasure.CompleteStep(ErasureModule.Organisation, now);
        await unitOfWork.PublishAsync(new PersonErasureRequestedV1(erasure.Id.Value, person.Value));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private static ErasureConfirmation Confirmation(IReadOnlyList<EmailAddress> addresses)
    {
        if (addresses.Count == 0)
        {
            return ErasureConfirmation.NoAddress;
        }

        return ErasureConfirmation.Mailed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No erasure {Erasure} is logged; its confirmation mail is dropped.")]
    private static partial void ErasureGone(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Information, Message = "The confirmation of erasure {Erasure} is handled already; it is not mailed again.")]
    private static partial void AlreadyHandled(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Erasure {Erasure} skipped membership {Membership}, which is invited or active again; its name and address are kept.")]
    private static partial void StillOpen(ILogger logger, Guid erasure, Guid membership);
}
