using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>A batch becomes its DTO in one place, <c>CourseAssignmentMappings</c>: identifiers per assigned person, a code and a message per refused one.</summary>
public class CourseAssignmentMappingsTests
{
    [Fact]
    public void A_batch_maps_each_assigned_and_refused_learner_with_its_code_and_message()
    {
        var anna = PersonId.From(Guid.CreateVersion7());
        var ben = PersonId.From(Guid.CreateVersion7());
        var clara = PersonId.From(Guid.CreateVersion7());
        var annasAssignment = AssignmentId.New();
        var bensAssignment = AssignmentId.New();
        var refusal = AssignmentFailures.AlreadyActive(AssignmentErrors.AlreadyActive(clara, CourseId.From(Guid.CreateVersion7())));
        var batch = new AssignmentBatch([new LearnerAssigned(anna, annasAssignment), new LearnerAssigned(ben, bensAssignment)], [new LearnerRefused(clara, refusal)]);

        var dto = batch.ToDto();

        dto.Assigned.ShouldBe([new ComplianceAssignedLearnerDto(anna.Value, annasAssignment.Value), new ComplianceAssignedLearnerDto(ben.Value, bensAssignment.Value)]);
        dto.Refused.ShouldBe([new ComplianceRefusedLearnerDto(clara.Value, "compliance.assignment-already-active", refusal.Message)]);
    }

    [Fact]
    public void An_empty_batch_maps_to_empty_lists()
    {
        var dto = new AssignmentBatch([], []).ToDto();

        dto.Assigned.ShouldBeEmpty();
        dto.Refused.ShouldBeEmpty();
    }
}
