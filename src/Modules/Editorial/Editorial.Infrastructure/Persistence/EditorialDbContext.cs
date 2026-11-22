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
    /// Writes each tracked version's chapter and question order, and each of its questions' option order, into their
    /// <c>position</c> columns first: the order is the aggregate's lists, which the Domain keeps without a position of its own.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        foreach (var version in ChangeTracker.Entries<CourseVersion>().Where(entry => entry.State != EntityState.Deleted).Select(entry => entry.Entity))
        {
            Number(version.Chapters, ChapterConfiguration.Position);
            Number(version.Questions, QuestionConfiguration.Position);
            foreach (var question in version.Questions)
            {
                Number(question.Options, AnswerOptionConfiguration.Position);
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Gives each entity its place in the list, counted from 1, where it differs from the one it has.</summary>
    private void Number<TEntity>(IReadOnlyList<TEntity> entities, string positionProperty)
        where TEntity : class
    {
        for (var index = 0; index < entities.Count; index++)
        {
            var position = Entry(entities[index]).Property<int>(positionProperty);
            if (position.CurrentValue != index + 1)
            {
                position.CurrentValue = index + 1;
            }
        }
    }
}
