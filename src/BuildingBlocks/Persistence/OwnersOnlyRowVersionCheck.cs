using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// Refuses, when the model is built, <c>ChangesOnlyItsOwnersRowVersion</c> on an entity type with more than one foreign
/// key, or on a property of one. <see cref="RowVersionBump"/> climbs from a marked change through its entity's foreign key
/// to the nearest row version; with two, which one is nearest would depend on the model's key order, and the change could
/// stop at another aggregate's row (docs/adr/0037).
/// </summary>
public sealed class OwnersOnlyRowVersionCheck : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        Ensure.That(modelBuilder).IsNotNull();

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var marked = entityType.ChangesOnlyItsOwnersRowVersion()
                ? [entityType.DisplayName()]
                : entityType.GetDeclaredProperties().Where(property => property.ChangesOnlyItsOwnersRowVersion()).Select(property => $"{entityType.DisplayName()}.{property.Name}").ToList();
            var foreignKeys = entityType.GetForeignKeys().Count();
            if (marked.Count > 0 && foreignKeys > 1)
            {
                throw new InvalidOperationException(
                    $"{string.Join(", ", marked)} changes only its owner's row version, but {entityType.DisplayName()} has {foreignKeys} foreign keys; the marker needs exactly one, so the nearest row version is unambiguous (docs/adr/0037).");
            }
        }
    }
}
