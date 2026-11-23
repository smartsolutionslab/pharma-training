using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

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
/// </summary>
public sealed class CertificateIssuedV1Consumer(IAssignmentStore assignments, IComplianceUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CertificateIssuedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CertificateId, nameof(message.CertificateId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        Ensure.That(message.IssuedAt, nameof(message.IssuedAt)).Satisfies(at => at != default, "must be set.");

        var active = await assignments.ActiveOfLearnerAsync(PersonId.From(message.PersonId), CourseId.From(message.CourseId), cancellationToken);
        if (active.Count == 0)
        {
            return;
        }

        var certificate = CertificateId.From(message.CertificateId);
        var issuedAt = Timestamp.From(message.IssuedAt);
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

        if (announced == 0)
        {
            return;
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
