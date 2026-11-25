using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence.Tests.RowVersions;

/// <summary>A module context over a model that uses the row-version marker, built without a database.</summary>
public sealed class MarkedModelContext(MarkedModel shape)
    : ModuleDbContext(
        new DbContextOptionsBuilder<MarkedModelContext>().UseNpgsql("Host=unused").ReplaceService<IModelCacheKeyFactory, MarkedModelCacheKeys>().Options,
        new ServiceCollection().BuildServiceProvider(),
        "marked")
{
    /// <summary>The model this context builds.</summary>
    public MarkedModel Shape { get; } = shape;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (Shape == MarkedModel.OwnedLinkWithOneKey)
        {
            modelBuilder.Entity<Parent>(parent =>
            {
                parent.HasRowVersion();
                parent.OwnsMany(p => p.Links, links =>
                {
                    links.Property<Guid>("ParentId");
                    links.WithOwner().HasForeignKey("ParentId");
                    links.HasKey("ParentId", nameof(Link.Hash));
                    links.ChangesOnlyItsOwnersRowVersion();
                });
            });
            return;
        }

        modelBuilder.Entity<Parent>().HasRowVersion().Ignore(p => p.Links);
        modelBuilder.Entity<Other>().HasRowVersion();
        modelBuilder.Entity<Child>(child =>
        {
            child.HasOne<Parent>().WithMany().HasForeignKey(c => c.ParentId);
            child.HasOne<Other>().WithMany().HasForeignKey(c => c.OtherId);
            if (Shape == MarkedModel.EntityWithTwoKeys)
            {
                child.HasAnnotation(RowVersion.OwnersOnlyAnnotation, true);
            }
            else
            {
                child.Property(c => c.Note).ChangesOnlyItsOwnersRowVersion();
            }
        });
    }
}
