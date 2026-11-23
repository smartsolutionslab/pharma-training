using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure;

/// <summary>The module's assignments. The module's unit of work saves what is added.</summary>
public sealed class AssignmentStore(ComplianceDbContext context) : IAssignmentStore
{
    public async Task<IReadOnlyList<Assignment>> InPharmacyAsync(PharmacyId pharmacy, CancellationToken cancellationToken) =>
        await context.Set<Assignment>()
            .AsNoTracking()
            .Where(assignment => assignment.Pharmacy == pharmacy)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<PersonId>> ActiveLearnersAsync(PharmacyId pharmacy, CourseId course, IReadOnlyCollection<PersonId> learners, CancellationToken cancellationToken)
    {
        Ensure.That(learners).IsNotNull();

        List<PersonId> asked = [.. learners];
        var found = await context.Set<Assignment>()
            .AsNoTracking()
            .Where(assignment => assignment.Pharmacy == pharmacy
                && assignment.Course == course
                && asked.Contains(assignment.Learner)
                && (assignment.Status == AssignmentStatus.Open || assignment.Status == AssignmentStatus.InProgress))
            .Select(assignment => assignment.Learner)
            .ToListAsync(cancellationToken);
        return found.ToHashSet();
    }

    public void Add(Assignment assignment) => context.Add(assignment);
}
