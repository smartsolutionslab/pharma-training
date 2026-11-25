using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>Records Compliance's part of an erasure as its step <c>compliance</c> (#115).</summary>
public sealed class ComplianceErasureCompletedV1Consumer(
    IErasureStore erasures,
    IOrganisationUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<ComplianceErasureCompletedV1Consumer> logger)
{
    private readonly IErasureStore erasures = erasures;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;
    private readonly ILogger<ComplianceErasureCompletedV1Consumer> logger = logger;

    public Task HandleAsync(ComplianceErasureCompletedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{erasures}{unitOfWork}{time}{logger}");
}
