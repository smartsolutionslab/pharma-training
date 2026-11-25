using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>Erases a person in Learning (#115). Not implemented yet: slice 3's red tests.</summary>
public sealed class PersonErasureRequestedV1Consumer(IPersonalMessagePurge purge, ILearningUnitOfWork unitOfWork)
{
    private readonly IPersonalMessagePurge purge = purge;
    private readonly ILearningUnitOfWork unitOfWork = unitOfWork;

    public Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{purge.GetType().Name}, {unitOfWork.GetType().Name}");
}
