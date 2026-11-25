using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Records Certification's part of an erasure as its step <c>certification</c> (#115). It is idempotent by the erasure's state:
/// a step that is recorded already stays as it first arrived. An erasure Organisation does not know is logged and dropped.
/// </summary>
public sealed partial class CertificationErasureCompletedV1Consumer(
    IErasureStore erasures,
    IOrganisationUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<CertificationErasureCompletedV1Consumer> logger)
{
    private static readonly ErasureModule Certification = ErasureModule.From("certification");

    private readonly IErasureStore erasures = erasures;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;
    private readonly ILogger<CertificationErasureCompletedV1Consumer> logger = logger;

    public async Task HandleAsync(CertificationErasureCompletedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.ErasureId, nameof(message.ErasureId)).IsNotEmpty();

        var found = await erasures.LoadAsync(ErasureId.From(message.ErasureId), cancellationToken);
        if (!found.HasValue)
        {
            ErasureUnknown(logger, message.ErasureId);
            return;
        }

        found.Value.CompleteStep(Certification, Timestamp.From(time.GetUtcNow()));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No erasure {Erasure} is logged; Certification's completion is dropped.")]
    private static partial void ErasureUnknown(ILogger logger, Guid erasure);
}
