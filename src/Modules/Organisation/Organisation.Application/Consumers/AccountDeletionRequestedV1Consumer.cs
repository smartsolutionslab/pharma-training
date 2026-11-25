using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Deletes an erased person's Keycloak account and records the erasure's step <c>keycloak</c> (#115). It is idempotent by
/// the erasure's state: once the step is recorded, Keycloak is not asked again, and an account that is gone counts as
/// deleted. A client's service account is never deleted, nor is a staff account, one holding a realm role of
/// <see cref="StaffRoles"/> (owner decision on #115, docs/adr/0041): each logs an error by the erasure's id alone and
/// leaves the step open, so the erasure shows as unfinished until an administrator removes the account by hand. A failure of Keycloak records nothing and rethrows, so Wolverine
/// retries and finally dead-letters the message, which holds ids only.
/// </summary>
public sealed partial class AccountDeletionRequestedV1Consumer(
    IErasureStore erasures,
    IAccounts accounts,
    IOrganisationUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<AccountDeletionRequestedV1Consumer> logger)
{
    private readonly IErasureStore erasures = erasures;
    private readonly IAccounts accounts = accounts;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;
    private readonly ILogger<AccountDeletionRequestedV1Consumer> logger = logger;

    public async Task HandleAsync(AccountDeletionRequestedV1 message, CancellationToken cancellationToken)
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
        if (erasure.HasCompleted(ErasureModule.Keycloak))
        {
            AlreadyDeleted(logger, message.ErasureId);
            return;
        }

        var person = PersonId.From(message.PersonId);
        var account = await accounts.FindAsync(person, cancellationToken);
        if (account.HasValue)
        {
            if (account.Value.IsServiceAccount)
            {
                // A person's id never names a client's service account; one that does is a mistake support must see.
                ServiceAccount(logger, message.ErasureId);
                return;
            }

            if (account.Value.HoldsStaffRole)
            {
                // A staff account may be the one that requested the erasure or that runs the platform; only an
                // administrator who checked it removes it (README runbook).
                StaffAccount(logger, message.ErasureId);
                return;
            }

            await accounts.DeleteAsync(person, cancellationToken);
        }

        erasure.CompleteStep(ErasureModule.Keycloak, Timestamp.From(time.GetUtcNow()));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No erasure {Erasure} is logged; its account's deletion is dropped.")]
    private static partial void ErasureGone(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Information, Message = "The account of erasure {Erasure} is deleted already; Keycloak is not asked again.")]
    private static partial void AlreadyDeleted(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erasure {Erasure} names a client's service account in Keycloak, which is never deleted; its step keycloak stays open.")]
    private static partial void ServiceAccount(ILogger logger, Guid erasure);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erasure {Erasure} names a staff account in Keycloak (a realm role support, editor or manager), which an erasure never deletes; its step keycloak stays open until an administrator removes the account.")]
    private static partial void StaffAccount(ILogger logger, Guid erasure);
}
