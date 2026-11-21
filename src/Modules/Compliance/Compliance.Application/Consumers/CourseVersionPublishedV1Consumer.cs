using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Stores a course version Editorial published, once, without its chapters, which Compliance does not keep. A version is
/// published once, so a version already stored means its event was handled, whatever the event's identifier or
/// payload: a second delivery changes nothing, and the first one wins.
/// It deduplicates on the version id, not on <c>Metadata.EventIdentifier</c>: for a fact only ever inserted that is stronger, since it also absorbs a re-publish under a new identifier.
/// </summary>
public sealed class CourseVersionPublishedV1Consumer(ICourseVersionStore versions, IComplianceUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CourseVersionPublishedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        ConsumerGuards.IsText(message.Title, nameof(message.Title), CourseTitle.MaxLength);
        Ensure.That(message.VersionNumber, nameof(message.VersionNumber)).AtLeast(1);
        Ensure.That(message.ValidityMonths, nameof(message.ValidityMonths)).AtLeast(1);

        var version = CourseVersionId.From(message.CourseVersionId);
        if (await versions.ExistsAsync(version, cancellationToken))
        {
            return;
        }

        // Backs the unique (course, number) index: a number its course already has under another id is a conflict no
        // retry resolves, refused here with the version it conflicts with rather than by the index.
        var course = CourseId.From(message.CourseId);
        var number = VersionNumber.From(message.VersionNumber);
        var stored = await versions.FindByNumberAsync(course, number, cancellationToken);
        if (stored.HasValue)
        {
            throw new InvalidOperationException(
                $"Course version {version} of course {course} has number {number}, which version {stored.Value} of that course already has.");
        }

        versions.Add(CourseVersion.Published(
            version,
            course,
            number,
            CourseTitle.From(message.Title),
            ValidityMonths.From(message.ValidityMonths)));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
