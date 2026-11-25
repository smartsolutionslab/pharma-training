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
    public async Task<Option<Assignment>> ReadAsync(AssignmentId assignment, CancellationToken cancellationToken)
    {
        var found = await context.Set<Assignment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(stored => stored.Id == assignment, cancellationToken);
        if (found is null)
        {
            return Option<Assignment>.None;
        }

        return Option<Assignment>.Some(found);
    }

    public Task<bool> HasFollowUpAsync(AssignmentId assignment, CancellationToken cancellationToken) =>
        context.Set<Assignment>().AnyAsync(stored => stored.Follows == assignment, cancellationToken);

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

    public async Task<IReadOnlyList<Assignment>> ActiveOfLearnerAsync(PersonId learner, CourseId course, CancellationToken cancellationToken) =>
        await context.Set<Assignment>()
            .Where(assignment => assignment.Learner == learner
                && assignment.Course == course
                && (assignment.Status == AssignmentStatus.Open || assignment.Status == AssignmentStatus.InProgress))
            .ToListAsync(cancellationToken);

    public void Add(Assignment assignment) => context.Add(assignment);

    public async Task<IReadOnlyList<Assignment>> ActiveOfLearnerInEveryPharmacyAsync(PersonId learner, CancellationToken cancellationToken) =>
        await context.Set<Assignment>()
            .Where(assignment => assignment.Learner == learner
                && (assignment.Status == AssignmentStatus.Open || assignment.Status == AssignmentStatus.InProgress))
            .ToListAsync(cancellationToken);

    public async Task<Option<Assignment>> FindInPharmacyAsync(PharmacyId pharmacy, AssignmentId assignment, CancellationToken cancellationToken)
    {
        var found = await context.Set<Assignment>()
            .FirstOrDefaultAsync(stored => stored.Id == assignment && stored.Pharmacy == pharmacy, cancellationToken);
        return Found(found);
    }

    public async Task<Option<Assignment>> ReadInPharmacyAsync(PharmacyId pharmacy, AssignmentId assignment, CancellationToken cancellationToken)
    {
        var found = await context.Set<Assignment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(stored => stored.Id == assignment && stored.Pharmacy == pharmacy, cancellationToken);
        return Found(found);
    }

    public async Task<IReadOnlyList<Assignment>> ActiveOfLearnerInPharmacyAsync(PersonId learner, PharmacyId pharmacy, CancellationToken cancellationToken) =>
        await context.Set<Assignment>()
            .Where(assignment => assignment.Learner == learner
                && assignment.Pharmacy == pharmacy
                && (assignment.Status == AssignmentStatus.Open || assignment.Status == AssignmentStatus.InProgress))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Assignment>> ReadActiveOfLearnerInPharmacyAsync(PersonId learner, PharmacyId pharmacy, CancellationToken cancellationToken) =>
        await context.Set<Assignment>()
            .AsNoTracking()
            .Where(assignment => assignment.Learner == learner
                && assignment.Pharmacy == pharmacy
                && (assignment.Status == AssignmentStatus.Open || assignment.Status == AssignmentStatus.InProgress))
            .ToListAsync(cancellationToken);

    private static Option<Assignment> Found(Assignment? assignment)
    {
        if (assignment is null)
        {
            return Option<Assignment>.None;
        }

        return Option<Assignment>.Some(assignment);
    }
}
