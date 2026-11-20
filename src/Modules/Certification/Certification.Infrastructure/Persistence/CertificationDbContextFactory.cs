using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> only: builds the context without the application or a database.</summary>
public sealed class CertificationDbContextFactory : IDesignTimeDbContextFactory<CertificationDbContext>
{
    public CertificationDbContext CreateDbContext(string[] args)
    {
        const string connectionString = "Server=localhost";
        const string schema = CertificationDbContext.Schema;
        var options = new DbContextOptionsBuilder<CertificationDbContext>();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        return new CertificationDbContext(options.Options, NoServices.Instance);
    }
}
