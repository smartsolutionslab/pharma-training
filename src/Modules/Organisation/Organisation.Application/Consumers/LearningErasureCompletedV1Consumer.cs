using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>Records Learning's part of an erasure as its step (#115). Not implemented yet: slice 3's red tests.</summary>
public sealed class LearningErasureCompletedV1Consumer(
    IErasureStore erasures,
    IOrganisationUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<LearningErasureCompletedV1Consumer> logger)
{
    private readonly IErasureStore erasures = erasures;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;
    private readonly ILogger<LearningErasureCompletedV1Consumer> logger = logger;

    public Task HandleAsync(LearningErasureCompletedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{erasures.GetType().Name}, {unitOfWork.GetType().Name}, {time.GetType().Name}, {logger.GetType().Name}");
}
