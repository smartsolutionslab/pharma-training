using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>Records Editorial's part of an erasure as its step (#115). Not implemented yet: slice 3's red tests.</summary>
public sealed class EditorialErasureCompletedV1Consumer(
    IErasureStore erasures,
    IOrganisationUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<EditorialErasureCompletedV1Consumer> logger)
{
    private readonly IErasureStore erasures = erasures;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;
    private readonly ILogger<EditorialErasureCompletedV1Consumer> logger = logger;

    public Task HandleAsync(EditorialErasureCompletedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{erasures.GetType().Name}, {unitOfWork.GetType().Name}, {time.GetType().Name}, {logger.GetType().Name}");
}
