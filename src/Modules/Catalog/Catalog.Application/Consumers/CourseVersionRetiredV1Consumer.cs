using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Consumers;

/// <summary>
/// Stores that Editorial retired a course version, once, whether the version has arrived yet or not: the version reads
/// as retired whenever it does.
/// </summary>
public sealed class CourseVersionRetiredV1Consumer(ICourseVersionStore versions, ICatalogUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CourseVersionRetiredV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CourseId).IsNotEmpty();
        Ensure.That(message.CourseVersionId).IsNotEmpty();

        if (await versions.IsRetiredAsync(message.CourseVersionId, cancellationToken))
        {
            return;
        }

        versions.Add(new CourseVersionRetirement { CourseVersionId = message.CourseVersionId, CourseId = message.CourseId });
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
