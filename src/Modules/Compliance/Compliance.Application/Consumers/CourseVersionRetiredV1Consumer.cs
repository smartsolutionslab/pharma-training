using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Stores that Editorial retired a course version, once, whether the version has arrived yet or not: the version reads
/// as retired whenever it is there.
/// It deduplicates on the version id, not on <c>Metadata.EventIdentifier</c>: for a fact only ever inserted that is stronger, since it also absorbs a re-publish under a new identifier.
/// </summary>
public sealed class CourseVersionRetiredV1Consumer(ICourseVersionStore versions, IComplianceUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CourseVersionRetiredV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();

        var version = CourseVersionId.From(message.CourseVersionId);
        if (await versions.IsRetiredAsync(version, cancellationToken))
        {
            return;
        }

        versions.Add(CourseVersionRetirement.Of(version, CourseId.From(message.CourseId)));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
