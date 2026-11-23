using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// The assignments table in memory, and the module's unit of work over it: what is added is stored, and what is
/// published is sent, only once committed. An assignment is active while it is open or in progress, as the partial
/// unique index counts it. <see cref="LoseNextCommit"/> makes the next <see cref="TryCommitAsync"/> lose a race, as a
/// save does that deadlocked with another or whose rows another save changed.
/// </summary>
internal sealed class InMemoryAssignmentStore : IAssignmentStore, IComplianceUnitOfWork
{
    private readonly List<Assignment> added = [];
    private readonly List<IntegrationEvent> queued = [];
    private bool loseNextCommit;

    public List<Assignment> Stored { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Commits { get; private set; }

    public Task<IReadOnlyList<Assignment>> InPharmacyAsync(PharmacyId pharmacy, CancellationToken cancellationToken)
    {
        IReadOnlyList<Assignment> inPharmacy = [.. Stored.Where(assignment => assignment.Pharmacy == pharmacy)];
        return Task.FromResult(inPharmacy);
    }

    public Task<IReadOnlySet<PersonId>> ActiveLearnersAsync(PharmacyId pharmacy, CourseId course, IReadOnlyCollection<PersonId> learners, CancellationToken cancellationToken)
    {
        IReadOnlySet<PersonId> active = Stored
            .Where(assignment => assignment.Pharmacy == pharmacy
                && assignment.Course == course
                && learners.Contains(assignment.Learner)
                && assignment.Status is AssignmentStatus.Open or AssignmentStatus.InProgress)
            .Select(assignment => assignment.Learner)
            .ToHashSet();
        return Task.FromResult(active);
    }

    public Task<IReadOnlyList<Assignment>> ActiveOfLearnerAsync(PersonId learner, CourseId course, CancellationToken cancellationToken)
    {
        IReadOnlyList<Assignment> active = Stored
            .Where(assignment => assignment.Learner == learner
                && assignment.Course == course
                && assignment.Status is AssignmentStatus.Open or AssignmentStatus.InProgress)
            .ToList();
        return Task.FromResult(active);
    }

    public void Add(Assignment assignment) => added.Add(assignment);

    public Task<IReadOnlyList<Assignment>> ActiveOfLearnerInPharmacyAsync(PersonId learner, PharmacyId pharmacy, CancellationToken cancellationToken)
    {
        IReadOnlyList<Assignment> active =
        [
            .. Stored.Where(assignment => assignment.Learner == learner
                && assignment.Pharmacy == pharmacy
                && assignment.Status is AssignmentStatus.Open or AssignmentStatus.InProgress),
        ];
        return Task.FromResult(active);
    }

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        queued.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Stored.AddRange(added);
        added.Clear();
        Published.AddRange(queued);
        queued.Clear();
        Commits++;
        return Task.CompletedTask;
    }

    /// <summary>The next <see cref="TryCommitAsync"/> answers <see langword="false"/> and stores and sends nothing.</summary>
    public void LoseNextCommit() => loseNextCommit = true;

    public async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        if (loseNextCommit)
        {
            loseNextCommit = false;
            added.Clear();
            queued.Clear();
            return false;
        }

        await CommitAsync(cancellationToken);
        return true;
    }
}
