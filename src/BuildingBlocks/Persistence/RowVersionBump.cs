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
        var waiting = new Queue<EntityEntry>(tracked.Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted));
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
