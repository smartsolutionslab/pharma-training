using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// Organisation's model, without a database. <see cref="InvitationLink"/> is an <c>IValueObject</c>, so the convention
/// that maps value objects (src/BuildingBlocks/Persistence, <c>ValueObjectConventions.MapValueObjects</c>) registers a
/// conversion to its hash for it too. That is harmless only while the link is stored as an owned row of its membership.
/// A scalar property of the type left to the conventions fails while EF builds the model (the hash is a value object no
/// provider maps), so every test here throws; one a configuration maps with an explicit <c>HasConversion</c> to the hash's
/// string builds, and would keep just the hash in one column. These tests pin both (#198's review, #401's).
/// </summary>
public sealed class InvitationLinkModelTests : IDisposable
{
    private readonly OrganisationDbContext context = new OrganisationDbContextFactory().CreateDbContext([]);

    [Fact]
    public void An_invitation_link_is_a_row_of_invitation_links_owned_by_its_membership()
    {
        var link = context.Model.FindEntityType(typeof(InvitationLink));
        link.ShouldNotBeNull("InvitationLink is not mapped as an entity type of its own");

        link.IsOwned().ShouldBeTrue();
        link.FindOwnership().ShouldNotBeNull().PrincipalEntityType.ClrType.ShouldBe(typeof(Membership));
        link.GetTableName().ShouldBe("invitation_links");
        link.FindProperty(nameof(InvitationLink.Hash)).ShouldNotBeNull().GetColumnName().ShouldBe("token_hash");
    }

    /// <summary>
    /// Fails on its assertion for <c>public InvitationLink? Latest { get; private set; }</c> on <see cref="Membership"/>
    /// mapped with <c>builder.Property(membership =&gt; membership.Latest).HasConversion(link =&gt; link!.Hash.Value, hash =&gt;
    /// InvitationLink.From(InvitationTokenHash.From(hash)))</c>, naming <c>Membership.Latest (InvitationLink)</c>.
    /// </summary>
    [Fact]
    public void No_property_stores_a_type_that_is_mapped_as_rows_in_one_column()
    {
        var entities = context.Model.GetEntityTypes().Select(entity => entity.ClrType).ToHashSet();

        var scalars = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => (Entity: entity, Property: property)))
            .Where(candidate => entities.Contains(candidate.Property.ClrType))
            .Select(candidate => $"{candidate.Entity.DisplayName()}.{candidate.Property.Name} ({candidate.Property.ClrType.Name})");

        scalars.ShouldBeEmpty();
    }

    public void Dispose() => context.Dispose();
}
