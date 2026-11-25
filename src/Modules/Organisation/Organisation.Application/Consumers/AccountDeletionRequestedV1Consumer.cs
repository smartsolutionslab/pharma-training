using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Deletes an erased person's Keycloak account and records the erasure's step <c>keycloak</c> (#115). It is idempotent by
/// the erasure's state: once the step is recorded, Keycloak is not asked again, and an account that is gone counts as
/// deleted. A client's service account is never deleted. A failure of Keycloak records nothing and rethrows, so Wolverine
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

    public Task HandleAsync(AccountDeletionRequestedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
