using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>Records Certification's part of an erasure as its step (#115). Not implemented yet: slice 4's red tests.</summary>
public sealed class CertificationErasureCompletedV1Consumer(
    IErasureStore erasures,
    IOrganisationUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<CertificationErasureCompletedV1Consumer> logger)
{
    private readonly IErasureStore erasures = erasures;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;
    private readonly ILogger<CertificationErasureCompletedV1Consumer> logger = logger;

    public Task HandleAsync(CertificationErasureCompletedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{erasures.GetType().Name}, {unitOfWork.GetType().Name}, {time.GetType().Name}, {logger.GetType().Name}");
}
