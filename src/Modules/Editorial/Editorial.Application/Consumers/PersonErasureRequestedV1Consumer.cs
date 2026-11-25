using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Consumers;

/// <summary>Erases a person in Editorial (#115). Not implemented yet: slice 3's red tests.</summary>
public sealed class PersonErasureRequestedV1Consumer(IReviewerNames names, IEditorialUnitOfWork unitOfWork)
{
    private readonly IReviewerNames names = names;
    private readonly IEditorialUnitOfWork unitOfWork = unitOfWork;

    public Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{names.GetType().Name}, {unitOfWork.GetType().Name}");
}
