using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.IntegrationEvents;

/// <summary>How the module's domain events are announced to other modules; pure, no I/O.</summary>
public static class ComplianceIntegrationEventMappings
{
    public static CourseAssignedV1 ToIntegrationEvent(this CourseAssigned assigned)
    {
        Ensure.That(assigned).IsNotNull();

        return new CourseAssignedV1(assigned.Assignment.Value, assigned.Learner.Value, assigned.Pharmacy.Value, assigned.Course.Value, assigned.DueDate.Value);
    }

    /// <summary>Late when the completion's day in Germany lies after the due date, as the dashboard's CompletedLate.</summary>
    public static AssignmentCompletedV1 ToIntegrationEvent(this AssignmentCompleted completed)
    {
        Ensure.That(completed).IsNotNull();

        return new AssignmentCompletedV1(
            completed.Assignment.Value,
            completed.Learner.Value,
            completed.Pharmacy.Value,
            completed.Course.Value,
            completed.Certificate.Value,
            completed.CompletedAt.Value,
            completed.DueDate.IsMissedBy(completed.CompletedAt));
    }

    /// <summary>Compliance's own request for the follow-up of the completed assignment; it carries the id only.</summary>
    public static AssignmentFollowUpRequestedV1 ToFollowUpRequest(this AssignmentCompleted completed)
    {
        Ensure.That(completed).IsNotNull();

        return new AssignmentFollowUpRequestedV1(completed.Assignment.Value);
    }
}
