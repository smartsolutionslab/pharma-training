using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Stores that Editorial retired a course version, once, whether the version has arrived yet or not: the version reads
/// as retired whenever it is there.
/// It deduplicates on the version id, not on <c>Metadata.EventIdentifier</c>: for a fact only ever inserted that is stronger, since it also absorbs a re-publish under a new identifier.
/// The primary key backs the check: of two deliveries racing, the loser's insert fails and its retry finds the row.
/// </summary>
public sealed class CourseVersionRetiredV1Consumer(ICourseVersionCopies versions, ILearningUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CourseVersionRetiredV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();

        if (await versions.IsRetiredAsync(CourseVersionId.From(message.CourseVersionId), cancellationToken))
        {
            return;
        }

        versions.Add(new CourseVersionRetirement
        {
            CourseVersionId = message.CourseVersionId,
            CourseId = message.CourseId,
        });
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
