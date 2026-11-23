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
}
