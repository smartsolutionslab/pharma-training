using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SetTestBlueprint;

// Red-first stub for #17: answers success and changes nothing until the change lands.
public sealed class SetTestBlueprintHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<SetTestBlueprint, Result<Unit, ApiError>>
{
    public Task<Result<Unit, ApiError>> HandleAsync(SetTestBlueprint command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        Ensure.That(versions).IsNotNull();
        Ensure.That(unitOfWork).IsNotNull();
        Result<Unit, ApiError> unchanged = Success(Unit.Value);
        return Task.FromResult(unchanged);
    }
}
