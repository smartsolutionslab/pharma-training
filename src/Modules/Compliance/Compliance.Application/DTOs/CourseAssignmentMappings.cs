using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

public static class CourseAssignmentMappings
{
    public static ComplianceCourseAssignmentDto ToDto(this AssignmentBatch batch)
    {
        Ensure.That(batch).IsNotNull();

        return new ComplianceCourseAssignmentDto(
            [.. batch.Assigned.Select(assigned => new ComplianceAssignedLearnerDto(assigned.Learner.Value, assigned.Assignment.Value))],
            [.. batch.Refused.Select(refused => new ComplianceRefusedLearnerDto(refused.Learner.Value, refused.Reason.Code, refused.Reason.Message))]);
    }
}
