using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> only: builds the context without the application or a database.</summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        const string connectionString = "Server=localhost";
        const string schema = CatalogDbContext.Schema;
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        return new CatalogDbContext(options.Options, NoServices.Instance);
    }
}
