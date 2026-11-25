using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>Erases a person in Compliance (#115) and announces it as <c>ComplianceErasureCompletedV1</c>.</summary>
public sealed class PersonErasureRequestedV1Consumer(
    IMemberStore members,
    IErasedPersonStore erased,
    IAssignmentStore assignments,
    IPersonalMessagePurge purge,
    IComplianceUnitOfWork unitOfWork,
    TimeProvider time)
{
    private readonly IMemberStore members = members;
    private readonly IErasedPersonStore erased = erased;
    private readonly IAssignmentStore assignments = assignments;
    private readonly IPersonalMessagePurge purge = purge;
    private readonly IComplianceUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{members}{erased}{assignments}{purge}{unitOfWork}{time}");
}
