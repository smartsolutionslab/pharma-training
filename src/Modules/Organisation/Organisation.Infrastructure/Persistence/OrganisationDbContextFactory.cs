using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> only: builds the context without the application or a database.</summary>
public sealed class OrganisationDbContextFactory : IDesignTimeDbContextFactory<OrganisationDbContext>
{
    public OrganisationDbContext CreateDbContext(string[] args)
    {
        const string connectionString = "Server=localhost";
        const string schema = OrganisationDbContext.Schema;
        var options = new DbContextOptionsBuilder<OrganisationDbContext>();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        return new OrganisationDbContext(options.Options, NoServices.Instance);
    }
}
