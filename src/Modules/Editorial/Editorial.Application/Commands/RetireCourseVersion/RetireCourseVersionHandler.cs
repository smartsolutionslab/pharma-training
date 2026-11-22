using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RetireCourseVersion;

/// <summary>
/// Retires the version and announces it with <see cref="CourseVersionRetiredV1"/> through the unit of work, so the
/// event is sent only with a commit that succeeds (ADR 0002: a lost race sends nothing).
/// </summary>
public sealed class RetireCourseVersionHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<RetireCourseVersion, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(RetireCourseVersion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var version = found.Value;
        var retired = version.Retire(command.Editor);
        if (retired.IsFailure)
        {
            return Failure(CourseVersionFailures.From(retired.Error));
        }

        await unitOfWork.PublishAsync(new CourseVersionRetiredV1(version.Course.Value, version.Id.Value));

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
