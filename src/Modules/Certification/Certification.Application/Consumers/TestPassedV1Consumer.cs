using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>
/// Issues the certificate for a passed test, once per attempt, from Certification's copies of the person's membership
/// and the course version, and announces it as <c>CertificateIssuedV1</c> in the same commit.
/// It deduplicates on the attempt id, not on <c>Metadata.EventIdentifier</c>: that also absorbs a re-publish under a new
/// identifier, and the unique index on the attempt refuses a concurrent delivery's second certificate, whose retry then
/// finds the first.
/// A copy that has not arrived yet, because the event overtook it, throws: Wolverine retries with backoff, and
/// dead-letters it to a replayable row once the retries run out.
/// </summary>
public sealed class TestPassedV1Consumer(
    ICertificateStore certificates,
    IMembershipStore memberships,
    ICourseVersionStore courseVersions,
    ICertificationUnitOfWork unitOfWork)
{
    public async Task HandleAsync(TestPassedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.AttemptId, nameof(message.AttemptId)).IsNotEmpty();
        Ensure.That(message.SessionId, nameof(message.SessionId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        Ensure.That(message.ScorePercent, nameof(message.ScorePercent)).InRange(ScorePercent.Minimum, ScorePercent.Maximum);

        var attempt = AttemptId.From(message.AttemptId);
        if (await certificates.ExistsForAttemptAsync(attempt, cancellationToken))
        {
            return;
        }

        var person = PersonId.From(message.PersonId);
        var holderName = await memberships.FindHolderNameAsync(person, cancellationToken);
        if (!holderName.HasValue)
        {
            throw new InvalidOperationException($"No membership of person {person} has arrived yet to name the holder of attempt {attempt}.");
        }

        var course = CourseId.From(message.CourseId);
        var versionId = CourseVersionId.From(message.CourseVersionId);
        var stored = await courseVersions.FindAsync(versionId, cancellationToken);
        if (!stored.HasValue)
        {
            throw new InvalidOperationException($"Course version {versionId} of attempt {attempt} has not arrived yet.");
        }

        var version = stored.Value;
        if (version.Course != course)
        {
            throw new InvalidOperationException(
                $"Attempt {attempt} passed course version {versionId} of course {course}, but that version belongs to course {version.Course}.");
        }

        var certificate = Certificate.Issue(
            CertificateId.New(),
            attempt,
            person,
            holderName.Value,
            course,
            versionId,
            version.Title,
            version.Validity,
            ScorePercent.From(message.ScorePercent),
            VerificationCodes.Next(),
            Timestamp.From(message.PassedAt));
        certificates.Add(certificate);
        foreach (var issued in certificate.DomainEvents.OfType<CertificateIssued>())
        {
            await unitOfWork.PublishAsync(issued.ToIntegrationEvent());
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
