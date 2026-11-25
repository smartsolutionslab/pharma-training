using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// An aggregate root's row version: a shadow <see cref="uint"/> property mapped to PostgreSQL's <c>xmin</c> system
/// column, which every UPDATE of the row moves on. EF Core checks it on every update and delete of the row, so a save
/// that loaded the row before another save changed it updates no row and fails with a
/// <see cref="DbUpdateConcurrencyException"/>. <c>RowVersionBump</c> updates the root whenever an entity under it
/// changes, so a change to its children alone is checked too. The Domain never sees it.
/// </summary>
public static class RowVersion
{
    /// <summary>The shadow property's name; its column is <c>xmin</c>.</summary>
    public const string PropertyName = "RowVersion";

    /// <summary>
    /// Gives the entity a row version: call it on an aggregate root's configuration, or on an entity under it whose
    /// changes are kept to its own row (<see cref="ChangesOnlyItsOwnersRowVersion{TProperty}"/>).
    /// </summary>
    public static EntityTypeBuilder<T> HasRowVersion<T>(this EntityTypeBuilder<T> builder)
        where T : class
    {
        Ensure.That(builder).IsNotNull();

        // Npgsql maps a uint row version to xmin (type xid). A system column: its migration adds no column.
        builder.Property<uint>(PropertyName).IsRowVersion();
        return builder;
    }

    /// <summary>The annotation that keeps a change to its nearest row version (<see cref="ChangesOnlyItsOwnersRowVersion{TOwner, TDependent}"/>).</summary>
    public const string OwnersOnlyAnnotation = "PharmaTraining:ChangesOnlyItsOwnersRowVersion";

    /// <summary>
    /// Keeps changes to this property to the row version of the entity that holds it, when the entity has one: saving
    /// only this property checks and moves that entity's row, not its aggregate root's (docs/adr/0036).
    /// </summary>
    public static PropertyBuilder<TProperty> ChangesOnlyItsOwnersRowVersion<TProperty>(this PropertyBuilder<TProperty> builder)
    {
        Ensure.That(builder).IsNotNull();
        builder.HasAnnotation(OwnersOnlyAnnotation, true);
        return builder;
    }

    /// <summary>
    /// Keeps adding, changing and removing these owned entities to the nearest row version above them: saving them checks
    /// and moves their owner's row (or the first owner up with a row version), not its aggregate root's (docs/adr/0036).
    /// </summary>
    public static OwnedNavigationBuilder<TOwner, TDependent> ChangesOnlyItsOwnersRowVersion<TOwner, TDependent>(this OwnedNavigationBuilder<TOwner, TDependent> builder)
        where TOwner : class
        where TDependent : class
    {
        Ensure.That(builder).IsNotNull();
        builder.HasAnnotation(OwnersOnlyAnnotation, true);
        return builder;
    }

    /// <summary>Whether a change to this property or entity type stops at its nearest row version.</summary>
    public static bool ChangesOnlyItsOwnersRowVersion(this IReadOnlyAnnotatable annotatable)
    {
        Ensure.That(annotatable).IsNotNull();
        return annotatable.FindAnnotation(OwnersOnlyAnnotation)?.Value is true;
    }

    /// <summary>Whether entities of this type carry a row version.</summary>
    public static bool HasRowVersion(this IReadOnlyEntityType entityType)
    {
        Ensure.That(entityType).IsNotNull();
        return entityType.FindProperty(PropertyName) is { IsConcurrencyToken: true };
    }
}
