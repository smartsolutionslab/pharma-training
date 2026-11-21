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

    /// <summary>Gives the entity a row version: call it on an aggregate root's configuration.</summary>
    public static EntityTypeBuilder<T> HasRowVersion<T>(this EntityTypeBuilder<T> builder)
        where T : class
    {
        Ensure.That(builder).IsNotNull();

        // Npgsql maps a uint row version to xmin (type xid). A system column: its migration adds no column.
        builder.Property<uint>(PropertyName).IsRowVersion();
        return builder;
    }

    /// <summary>Whether entities of this type carry a row version.</summary>
    public static bool HasRowVersion(this IReadOnlyEntityType entityType)
    {
        Ensure.That(entityType).IsNotNull();
        return entityType.FindProperty(PropertyName) is { IsConcurrencyToken: true };
    }
}
