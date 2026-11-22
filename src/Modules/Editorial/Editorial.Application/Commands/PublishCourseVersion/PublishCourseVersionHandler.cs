using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.PublishCourseVersion;

/// <summary>
/// Publishes the version and supersedes the course's published versions in one save (ADR 0007), so a course never
/// keeps two: a predecessor changed by another save since it was loaded makes the commit lose its race, and nothing
/// is saved. Only a publish that succeeds supersedes anything.
/// </summary>
public sealed class PublishCourseVersionHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, ReviewSettings review, TimeProvider time)
    : ICommandHandler<PublishCourseVersion, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(PublishCourseVersion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var version = found.Value;
        var predecessors = await versions.ListPublishedForUpdateAsync(version.Course, cancellationToken);

        var published = version.Publish(command.Editor, review.Policy, Timestamp.From(time.GetUtcNow()));
        if (published.IsFailure)
        {
            return Failure(CourseVersionFailures.From(published.Error));
        }

        foreach (var predecessor in predecessors.Where(predecessor => predecessor.Id != version.Id))
        {
            predecessor.Supersede();
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(CourseVersionFailures.Changed(command.Version.Value));
        }

        return Success(Unit.Value);
    }
}
