using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, IServiceProvider services)
    : ModuleDbContext(options, services, Schema)
{
    /// <summary>The module's tables and its migrations history live in this schema.</summary>
    public const string Schema = "catalog";
}
