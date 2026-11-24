using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SetTestBlueprint;

/// <summary>
/// Replaces a draft's blueprint. No question is added or removed, but every question loses its approval, so the
/// changed test is reviewed again. A save that lost a race to another change of the version answers that the version
/// changed.
/// </summary>
public sealed class SetTestBlueprintHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<SetTestBlueprint, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(SetTestBlueprint command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var set = found.Value.SetBlueprint(command.Blueprint, command.Editor);
        if (set.IsFailure)
        {
            return Failure(CourseVersionFailures.From(set.Error));
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
