using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RetireCourseVersion;

/// <summary>
/// Retires the version and announces it with <see cref="CourseVersionRetiredV1"/> through the unit of work, so the
/// event is sent only with a commit that succeeds (ADR 0002: a lost race sends nothing).
/// </summary>
public sealed class RetireCourseVersionHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, TimeProvider time)
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
        var retired = version.Retire(command.Editor, Timestamp.From(time.GetUtcNow()));
        if (retired.IsFailure)
        {
            return Failure(CourseVersionFailures.From(retired.Error));
        }

        await unitOfWork.PublishAsync(version.ToCourseVersionRetiredV1());

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
