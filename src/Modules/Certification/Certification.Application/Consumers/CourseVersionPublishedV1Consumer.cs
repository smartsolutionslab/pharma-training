using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>
/// Stores a course version Editorial published, once, with its course, number, title and validity; its chapters are
/// not kept. The number is printed on the certificate (#50). A version is published once, so a version already stored means its event was handled, whatever the event's
/// identifier or payload: a second delivery changes nothing, and the first one wins.
/// It deduplicates on the version id, not on <c>Metadata.EventIdentifier</c>: for a fact only ever inserted that is stronger, since it also absorbs a re-publish under a new identifier.
/// </summary>
public sealed class CourseVersionPublishedV1Consumer(ICourseVersionStore versions, ICertificationUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CourseVersionPublishedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        Ensure.That(message.VersionNumber, nameof(message.VersionNumber)).AtLeast(CourseVersionNumber.Minimum);
        ConsumerGuards.IsText(message.Title, nameof(message.Title), CourseTitle.MaxLength);
        Ensure.That(message.ValidityMonths, nameof(message.ValidityMonths)).InRange(RecurrenceInterval.MinimumMonths, RecurrenceInterval.MaximumMonths);

        var version = CourseVersionId.From(message.CourseVersionId);
        if (await versions.ExistsAsync(version, cancellationToken))
        {
            return;
        }

        versions.Add(CourseVersion.Published(
            version,
            CourseId.From(message.CourseId),
            CourseVersionNumber.From(message.VersionNumber),
            CourseTitle.From(message.Title),
            RecurrenceInterval.From(message.ValidityMonths)));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
