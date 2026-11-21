using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;

public sealed class EditorialDbContext(DbContextOptions<EditorialDbContext> options, IServiceProvider services)
    : ModuleDbContext(options, services, Schema)
{
    /// <summary>The module's tables and its migrations history live in this schema.</summary>
    public const string Schema = "editorial";

    /// <summary>
    /// Writes each tracked version's chapter order into their <c>position</c> column first: the order is the
    /// aggregate's list, which the Domain keeps without a position of its own.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        foreach (var version in ChangeTracker.Entries<CourseVersion>().Where(entry => entry.State != EntityState.Deleted).Select(entry => entry.Entity))
        {
            for (var index = 0; index < version.Chapters.Count; index++)
            {
                var position = Entry(version.Chapters[index]).Property<int>(ChapterConfiguration.Position);
                if (position.CurrentValue != index + 1)
                {
                    position.CurrentValue = index + 1;
                }
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
