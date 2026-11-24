using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Creates the follow-up of a completed recurring assignment, from the host's durable local queue, after the completing
/// commit saved the request (docs/adr/0026): same learner, pharmacy, course, recurrence and assigner, due one interval
/// after the completion's day in Germany, and announced as CourseAssignedV1 in the same commit. A retired course still
/// gets its follow-up, so no course store is asked.
/// <para>
/// It deduplicates on the completed assignment, not on <c>Metadata.EventIdentifier</c>: a follow-up records the
/// assignment it follows, so a redelivery finds it and does nothing; two deliveries handled at once both insert, the
/// unique index refuses the second, which throws and whose retry finds the follow-up. A learner who left the pharmacy, or
/// who already has an active assignment of the course there, gets none, and that is logged. An unknown, one-off or
/// uncompleted assignment is never requested a follow-up, so it throws: the request is retried, then dead-lettered.
/// </para>
/// </summary>
public sealed partial class AssignmentFollowUpRequestedV1Consumer(
    IAssignmentStore assignments,
    IMemberStore members,
    IComplianceUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<AssignmentFollowUpRequestedV1Consumer> logger)
{
    public async Task HandleAsync(AssignmentFollowUpRequestedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.AssignmentId, nameof(message.AssignmentId)).IsNotEmpty();

        var id = AssignmentId.From(message.AssignmentId);
        var found = await assignments.ReadAsync(id, cancellationToken);
        if (!found.HasValue)
        {
            throw new InvalidOperationException($"Assignment {id} was requested a follow-up but is not stored.");
        }

        if (await assignments.HasFollowUpAsync(id, cancellationToken))
        {
            return;
        }

        var completed = found.Value;
        var activeMembers = await members.ActiveMembersAsync(completed.Pharmacy, [completed.Learner], cancellationToken);
        if (!activeMembers.Contains(completed.Learner))
        {
            LogLearnerLeft(logger, id.Value, completed.Pharmacy.Value);
            return;
        }

        var alreadyActive = await assignments.ActiveLearnersAsync(completed.Pharmacy, completed.Course, [completed.Learner], cancellationToken);
        if (alreadyActive.Contains(completed.Learner))
        {
            LogAlreadyActive(logger, id.Value);
            return;
        }

        var followUp = completed.FollowUp(AssignmentId.New(), Timestamp.From(time.GetUtcNow()));
        if (!followUp.HasValue)
        {
            throw new InvalidOperationException($"Assignment {id} was requested a follow-up but is {completed.Status}, recurring: {completed.Recurrence is not null}.");
        }

        var next = followUp.Value;
        assignments.Add(next);
        foreach (var assigned in next.DomainEvents.OfType<CourseAssigned>())
        {
            await unitOfWork.PublishAsync(assigned.ToIntegrationEvent());
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No follow-up of assignment {Assignment}: the learner is no longer a member of pharmacy {Pharmacy}.")]
    private static partial void LogLearnerLeft(ILogger logger, Guid assignment, Guid pharmacy);

    [LoggerMessage(Level = LogLevel.Information, Message = "No follow-up of assignment {Assignment}: the learner has an active assignment of the course in the pharmacy.")]
    private static partial void LogAlreadyActive(ILogger logger, Guid assignment);
}
