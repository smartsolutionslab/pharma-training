using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure;

/// <summary>
/// Learning's copy of the memberships: their starts, changes and endings, in tables of their own. The module's unit of
/// work saves what is added; nothing is ever updated, so two deliveries of one event race only on its primary key.
/// Copied from Compliance's <c>Compliance.Infrastructure/MemberStore.cs</c>; whether a person is an active member is
/// decided by <see cref="PersonMemberships"/> from the rows read here.
/// </summary>
public sealed class MembershipCopies(LearningDbContext context) : IMembershipCopies
{
    public Task<bool> ExistsAsync(MembershipId membership, CancellationToken cancellationToken)
    {
        Ensure.That(membership).IsNotNull();

        return context.Set<MemberCopy>().AnyAsync(member => member.Id == membership.Value, cancellationToken);
    }

    public void Add(MemberCopy member)
    {
        Ensure.That(member).IsNotNull();

        context.Add(member);
    }

    public Task<bool> IsEndedAsync(MembershipId membership, CancellationToken cancellationToken)
    {
        Ensure.That(membership).IsNotNull();

        return context.Set<MembershipEndingCopy>().AnyAsync(ending => ending.Id == membership.Value, cancellationToken);
    }

    public void Add(MembershipEndingCopy ending)
    {
        Ensure.That(ending).IsNotNull();

        context.Add(ending);
    }

    public Task<bool> HasChangeAsync(MembershipId membership, MembershipVersion version, CancellationToken cancellationToken)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(version).IsNotNull();

        return context.Set<MembershipChangeCopy>()
            .AnyAsync(change => change.MembershipId == membership.Value && change.Version == version.Value, cancellationToken);
    }

    public void Add(MembershipChangeCopy change)
    {
        Ensure.That(change).IsNotNull();

        context.Add(change);
    }

    public async Task<PersonMemberships> OfPersonAsync(PersonId person, CancellationToken cancellationToken)
    {
        Ensure.That(person).IsNotNull();

        var started = await context.Set<MemberCopy>()
            .AsNoTracking()
            .Where(member => member.PersonId == person.Value)
            .ToListAsync(cancellationToken);
        var changed = await context.Set<MembershipChangeCopy>()
            .AsNoTracking()
            .Where(change => change.PersonId == person.Value)
            .ToListAsync(cancellationToken);
        var ended = await context.Set<MembershipEndingCopy>()
            .AsNoTracking()
            .Where(ending => ending.PersonId == person.Value)
            .ToListAsync(cancellationToken);
        return new PersonMemberships(person, started, changed, ended);
    }
}
