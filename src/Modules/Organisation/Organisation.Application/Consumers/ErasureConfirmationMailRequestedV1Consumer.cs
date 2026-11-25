using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Mails the erasure's confirmation to each of the person's addresses (or, where Organisation holds none, to their
/// Keycloak account's verified address, Q5), then forgets their name and addresses in every pharmacy, removes their
/// invitation recipients and sender, records the confirmation and Organisation's step, announces the erasure as
/// <see cref="PersonErasureRequestedV1"/> and requests the account's deletion as <see cref="AccountDeletionRequestedV1"/>,
/// in one commit (#115). A confirmation that is handled already is not mailed again.
/// <para>
/// It is idempotent by the erasure's state, not by <c>Metadata.EventIdentifier</c>: a send that failed must be sent
/// again, and the handled confirmation is the key. An address the mail server refuses for good (a 5xx reply to RCPT TO) is
/// given up and logged by the erasure's id only; the confirmation counts as mailed when any address took it. Any other
/// failed send forgets nothing and rethrows, so Wolverine retries and finally dead-letters the message, which holds ids
/// only. When a send returned and a later one or the commit failed,
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
    IAccounts accounts,
    TimeProvider time,
    ILogger<ErasureConfirmationMailRequestedV1Consumer> logger)
{
    private readonly IErasureStore erasures = erasures;
    private readonly IPharmacyStore pharmacies = pharmacies;
    private readonly IInvitationRecipientStore recipients = recipients;
    private readonly IInvitationSenderStore senders = senders;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly IMailSender mails = mails;
    private readonly IAccounts accounts = accounts;
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
        var confirmTo = addresses.Count > 0 ? addresses : await AccountAddressAsync(erasure.Id, person, cancellationToken);

        // Every mail is out before anything is forgotten: once the address is gone, nothing could mail it.
        // An address the server refuses for good is given up: no retry could reach it, and the erasure must go on.
        var mailed = 0;
        foreach (var address in confirmTo)
        {
            try
            {
                await mails.SendAsync(ErasureConfirmationMails.Compose(address), cancellationToken);
                mailed++;
            }
            catch (MailNotSentException refused) when (refused.RecipientRefused)
            {
                AddressRefused(logger, message.ErasureId);
            }
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
        erasure.ConfirmationHandled(Confirmation(mailed), now);
        erasure.CompleteStep(ErasureModule.Organisation, now);
        await unitOfWork.PublishAsync(new PersonErasureRequestedV1(erasure.Id.Value, person.Value));
        // The account's deletion is Organisation's own work item, in the same commit: a Keycloak outage then retries the
        // deletion alone, never the mails or the forgetting above (docs/adr/0041).
        await unitOfWork.PublishAsync(new AccountDeletionRequestedV1(erasure.Id.Value, person.Value));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Q5 (#115): where Organisation holds no address of the person, the confirmation goes to their Keycloak account's
    /// address, only where Keycloak verified it (docs/adr/0014); no account, or none verified, means no mail. That address
    /// is used for the mail alone, and nothing in Organisation is forgotten by it (owner decision C, docs/adr/0041). A
    /// read Keycloak fails, with an error answer or no connection at all, reads as no address: it is logged by the
    /// erasure's id alone and the erasure goes on, since the account's deletion retries Keycloak on its own.
    /// </summary>
    private async Task<IReadOnlyList<EmailAddress>> AccountAddressAsync(ErasureId erasure, PersonId person, CancellationToken cancellationToken)
    {
        Option<Account> account;
        try
        {
            account = await accounts.FindAsync(person, cancellationToken);
        }
        catch (Exception exception) when (exception is AccountsUnavailableException or HttpRequestException)
        {
            AccountUnread(logger, erasure.Value);
            return [];
        }

        if (account.HasValue && account.Value.VerifiedAddress.HasValue)
        {
            return [account.Value.VerifiedAddress.Value];
        }

        return [];
    }

    /// <summary>Mailed when at least one address took the mail; no address when none was held or every one was refused.</summary>
    private static ErasureConfirmation Confirmation(int mailed)
    {
        if (mailed == 0)
        {
            return ErasureConfirmation.NoAddress;
        }

        return ErasureConfirmation.Mailed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No erasure {Erasure} is logged; its confirmation mail is dropped.")]
    private static partial void ErasureGone(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mail server refused one address of erasure {Erasure} for good; its confirmation is given up for that address.")]
    private static partial void AddressRefused(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak could not be read for the account of erasure {Erasure}; its confirmation is not mailed, and the erasure goes on.")]
    private static partial void AccountUnread(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Information, Message = "The confirmation of erasure {Erasure} is handled already; it is not mailed again.")]
    private static partial void AlreadyHandled(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Erasure {Erasure} skipped membership {Membership}, which is invited or active again; its name and address are kept.")]
    private static partial void StillOpen(ILogger logger, Guid erasure, Guid membership);
}
