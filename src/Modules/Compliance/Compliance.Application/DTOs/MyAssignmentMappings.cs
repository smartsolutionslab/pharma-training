using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListMyAssignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

public static class MyAssignmentMappings
{
    /// <summary>The row on the learner's dashboard; without a version to open there is nothing to continue.</summary>
    public static ComplianceMyAssignmentDto ToDto(this MyAssignment assignment)
    {
        Ensure.That(assignment).IsNotNull();

        Guid? startVersion = assignment.Start.HasValue ? assignment.Start.Value.Version.Value : null;
        var continues = assignment.Start.HasValue && assignment.Start.Value.Kind == CourseStartKind.Resume;
        return new ComplianceMyAssignmentDto(
            assignment.Assignment.Id.Value,
            assignment.Assignment.Course.Value,
            assignment.Title.Value,
            assignment.State.ToDto(),
            assignment.Assignment.DueDate.Value,
            startVersion,
            continues);
    }
}
