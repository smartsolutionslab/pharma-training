using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> only: builds the context without the application or a database.</summary>
public sealed class EditorialDbContextFactory : IDesignTimeDbContextFactory<EditorialDbContext>
{
    public EditorialDbContext CreateDbContext(string[] args)
    {
        const string connectionString = "Server=localhost";
        const string schema = EditorialDbContext.Schema;
        var options = new DbContextOptionsBuilder<EditorialDbContext>();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        return new EditorialDbContext(options.Options, NoServices.Instance);
    }
}
