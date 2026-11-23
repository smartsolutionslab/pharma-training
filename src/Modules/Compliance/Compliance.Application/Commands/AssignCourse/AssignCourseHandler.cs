using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.AssignCourse;

/// <summary>
/// Assigns the course to each learner who is an active member of the pharmacy and has no active assignment of it there,
/// refusing the others by name, and announces each assignment (CourseAssignedV1) in the same commit. A due date in the
/// past refuses the whole request. A save that lost a race, or deadlocked with another, saves and announces nothing
/// (docs/adr/0002); one the partial unique index refuses answers 409 already-exists.
/// </summary>
public sealed class AssignCourseHandler(
    IMemberStore members,
    ICourseVersionStore courses,
    IAssignmentStore assignments,
    IComplianceUnitOfWork unitOfWork,
    TimeProvider time)
    : ICommandHandler<AssignCourse, Result<ComplianceCourseAssignmentDto, ApiError>>
{
    public async Task<Result<ComplianceCourseAssignmentDto, ApiError>> HandleAsync(AssignCourse command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        Ensure.That(command.Learners).IsNotNull();

        List<PersonId> learners = [.. command.Learners.Distinct()];
        if (learners.Count == 0)
        {
            return Failure(AssignmentFailures.NoLearners());
        }

        if (learners.Count > AssignCourse.MaximumLearners)
        {
            return Failure(AssignmentFailures.TooManyLearners(AssignCourse.MaximumLearners));
        }

        var access = await ComplianceAccess.RequireAsync(members, command.By, command.Pharmacy, Capability.AssignCourses, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var course = await courses.FindCourseAsync(command.Course, cancellationToken);
        if (!course.HasValue)
        {
            return Failure(AssignmentFailures.CourseNotFound(command.Course.Value));
        }

        if (!course.Value.CurrentVersion().HasValue)
        {
            return Failure(AssignmentFailures.CourseRetired(command.Course.Value));
        }

        var now = Timestamp.From(time.GetUtcNow());
        var activeMembers = await members.ActiveMembersAsync(command.Pharmacy, learners, cancellationToken);
        var alreadyActive = await assignments.ActiveLearnersAsync(command.Pharmacy, command.Course, learners, cancellationToken);

        List<Assignment> created = [];
        List<LearnerAssigned> assigned = [];
        List<LearnerRefused> refused = [];
        foreach (var learner in learners)
        {
            if (!activeMembers.Contains(learner))
            {
                refused.Add(new LearnerRefused(learner, AssignmentFailures.LearnerNotAMember(learner.Value)));
                continue;
            }

            if (alreadyActive.Contains(learner))
            {
                refused.Add(new LearnerRefused(learner, AssignmentFailures.AlreadyActive(AssignmentErrors.AlreadyActive(learner, command.Course))));
                continue;
            }

            var assignment = Assignment.Assign(AssignmentId.New(), learner, command.Pharmacy, command.Course, command.DueDate, command.Recurrence, command.By, now);
            if (assignment.IsFailure)
            {
                return Failure(AssignmentFailures.From(assignment.Error));
            }

            created.Add(assignment.Value);
            assigned.Add(new LearnerAssigned(learner, assignment.Value.Id));
        }

        if (created.Count > 0)
        {
            foreach (var assignment in created)
            {
                assignments.Add(assignment);
                foreach (var announced in assignment.DomainEvents.OfType<CourseAssigned>())
                {
                    await unitOfWork.PublishAsync(announced.ToIntegrationEvent());
                }
            }

            if (!await unitOfWork.TryCommitAsync(cancellationToken))
            {
                return Failure(AssignmentFailures.AssignmentsChanged());
            }
        }

        return Success(new AssignmentBatch(assigned, refused).ToDto());
    }
}
