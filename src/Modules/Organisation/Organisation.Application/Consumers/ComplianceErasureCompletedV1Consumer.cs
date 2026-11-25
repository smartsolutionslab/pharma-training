using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Records Compliance's part of an erasure as its step <c>compliance</c> (#115). It is idempotent by the erasure's state:
/// a step that is recorded already stays as it first arrived. An erasure Organisation does not know is logged and dropped.
/// </summary>
public sealed partial class ComplianceErasureCompletedV1Consumer(
    IErasureStore erasures,
    IOrganisationUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<ComplianceErasureCompletedV1Consumer> logger)
{
    private static readonly ErasureModule Compliance = ErasureModule.From("compliance");

    private readonly IErasureStore erasures = erasures;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;
    private readonly ILogger<ComplianceErasureCompletedV1Consumer> logger = logger;

    public async Task HandleAsync(ComplianceErasureCompletedV1 message, CancellationToken cancellationToken)
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

        found.Value.CompleteStep(Compliance, Timestamp.From(time.GetUtcNow()));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No erasure {Erasure} is logged; Compliance's completion is dropped.")]
    private static partial void ErasureUnknown(ILogger logger, Guid erasure);
}
