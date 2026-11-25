using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// Before a save, updates the row of every aggregate root with a row version (<see cref="RowVersion"/>) whose
/// entities the save adds, changes or removes, even when the root itself is unchanged: the root's UPDATE carries
/// <c>WHERE xmin = @loaded</c>, so of two saves that loaded the same aggregate the second updates no row and fails,
/// even when both only touched its children. It follows each changed entry's foreign keys to the tracked principals,
/// through principals without a row version, up to the roots. A context without a row version is left as it is.
/// <para>
/// A change marked <c>ChangesOnlyItsOwnersRowVersion</c> (an owned entity type, or a modified entry whose every changed
/// property is marked) goes up only to the first principal with a row version, updates that one and stops there, so it
/// races only with changes to that entity and not with every change to its root (docs/adr/0037).
/// </para>
/// </summary>
public sealed class RowVersionBump : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Ensure.That(eventData).IsNotNull();
        Bump(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Ensure.That(eventData).IsNotNull();
        Bump(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Bump(DbContext? context)
    {
        if (context is null || !context.Model.GetEntityTypes().Any(entityType => entityType.HasRowVersion()))
        {
            return;
        }

        // EF Core calls interceptors before it detects changes itself; without this the save's changes are not seen.
        context.ChangeTracker.DetectChanges();

        var tracked = context.ChangeTracker.Entries().ToList();
        var changed = tracked.Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        foreach (var entry in changed.Where(ChangesOnlyItsOwnersRowVersion))
        {
            BumpNearest(entry, tracked);
        }

        var waiting = new Queue<EntityEntry>(changed.Where(entry => !ChangesOnlyItsOwnersRowVersion(entry)));
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (waiting.TryDequeue(out var entry))
        {
            foreach (var principal in entry.Metadata.GetForeignKeys().Select(foreignKey => PrincipalOf(entry, foreignKey, tracked)).OfType<EntityEntry>())
            {
                if (!visited.Add(principal.Entity))
                {
                    continue;
                }

                if (principal.State == EntityState.Unchanged && principal.Metadata.HasRowVersion())
                {
                    principal.State = EntityState.Modified;
                }

                waiting.Enqueue(principal);
            }
        }
    }

    /// <summary>Whether the entry's change is kept to its nearest row version: its type is marked, or every property it changed is.</summary>
    private static bool ChangesOnlyItsOwnersRowVersion(EntityEntry entry)
    {
        if (entry.Metadata.ChangesOnlyItsOwnersRowVersion())
        {
            return true;
        }

        if (entry.State != EntityState.Modified)
        {
            return false;
        }

        var modified = entry.Properties.Where(property => property.IsModified && property.Metadata.Name != RowVersion.PropertyName).ToList();
        return modified.Count > 0 && modified.All(property => property.Metadata.ChangesOnlyItsOwnersRowVersion());
    }

    /// <summary>Updates the entry itself when it carries a row version, or else the first principal above it that does.</summary>
    private static void BumpNearest(EntityEntry entry, IReadOnlyList<EntityEntry> tracked)
    {
        var current = entry;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (seen.Add(current.Entity))
        {
            if (current.Metadata.HasRowVersion() && current.State != EntityState.Deleted)
            {
                if (current.State == EntityState.Unchanged)
                {
                    current.State = EntityState.Modified;
                }

                return;
            }

            if (current.Metadata.GetForeignKeys().Select(foreignKey => PrincipalOf(current, foreignKey, tracked)).OfType<EntityEntry>().FirstOrDefault() is not { } principal)
            {
                return;
            }

            current = principal;
        }
    }

    /// <summary>The tracked entry the foreign key points to, if any: a removed entry points where it pointed when loaded.</summary>
    private static EntityEntry? PrincipalOf(EntityEntry dependent, IForeignKey foreignKey, IReadOnlyList<EntityEntry> tracked)
    {
        var values = foreignKey.Properties
            .Select(property => dependent.State == EntityState.Deleted
                ? dependent.Property(property.Name).OriginalValue
                : dependent.Property(property.Name).CurrentValue)
            .ToList();
        if (values.Any(value => value is null))
        {
            return null;
        }

        var keys = foreignKey.PrincipalKey.Properties;
        return tracked.FirstOrDefault(candidate =>
            foreignKey.PrincipalEntityType.IsAssignableFrom(candidate.Metadata)
            && candidate.State != EntityState.Detached
            && keys.Select((key, index) => Equals(candidate.Property(key.Name).CurrentValue, values[index])).All(match => match));
    }
}
