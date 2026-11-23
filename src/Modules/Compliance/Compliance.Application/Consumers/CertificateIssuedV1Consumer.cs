using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Completes every open or in-progress assignment of the person for the certificate's course, in every pharmacy and
/// whichever version the certificate is for, and announces each completion as AssignmentCompletedV1 in the same commit.
/// A certificate issued before an assignment was made does not fulfil it: that assignment stays active, nothing is
/// announced for it, and nothing is thrown, since a retry could never change the answer.
/// It deduplicates by state, not on <c>Metadata.EventIdentifier</c>: only active assignments are loaded and completing
/// makes them inactive, so the same certificate again, also re-published under a new identifier, finds nothing to do.
/// Two deliveries handled at once both load the same rows; the second commit loses on the row version
/// (docs/adr/0002-lost-updates-answer-409-through-the-use-case.md) and throws, nothing of it is saved or sent, and the
/// retry finds nothing active.
/// It also keeps a copy of the certificate, whether or not it completes anything, from which the dashboard tells that a
/// session of the course is finished (docs/adr/0024); that copy is deduplicated on the certificate id and saved in the
/// same commit, and two deliveries at once race only on its primary key: the loser throws and its retry finds the copy.
/// </summary>
public sealed class CertificateIssuedV1Consumer(IAssignmentStore assignments, ILearnerProgressStore progress, IComplianceUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CertificateIssuedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CertificateId, nameof(message.CertificateId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        Ensure.That(message.IssuedAt, nameof(message.IssuedAt)).Satisfies(at => at != default, "must be set.");

        var certificate = CertificateId.From(message.CertificateId);
        var person = PersonId.From(message.PersonId);
        var course = CourseId.From(message.CourseId);
        var issuedAt = Timestamp.From(message.IssuedAt);
        var kept = false;
        if (!await progress.HasCertificateAsync(certificate, cancellationToken))
        {
            progress.Add(IssuedCertificate.Of(certificate, person, course, CourseVersionId.From(message.CourseVersionId), issuedAt));
            kept = true;
        }

        var active = await assignments.ActiveOfLearnerAsync(person, course, cancellationToken);
        var announced = 0;
        foreach (var assignment in active)
        {
            var completed = assignment.Complete(certificate, issuedAt);
            if (completed.IsFailure)
            {
                switch (completed.Error)
                {
                    case CertificatePredatesAssignment:
                        continue;
                    case AssignmentNotActive:
                    case DueDateInThePast:
                        throw new InvalidOperationException($"Assignment {assignment.Id} was loaded as active but refused certificate {certificate}: {completed.Error}.");
                }
            }

            foreach (var completion in assignment.DomainEvents.OfType<AssignmentCompleted>())
            {
                await unitOfWork.PublishAsync(completion.ToIntegrationEvent());
                announced++;
            }
        }

        if (!kept && announced == 0)
        {
            return;
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
