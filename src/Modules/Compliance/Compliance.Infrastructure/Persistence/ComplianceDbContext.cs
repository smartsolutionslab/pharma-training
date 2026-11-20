using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;

public sealed class ComplianceDbContext(DbContextOptions<ComplianceDbContext> options, IServiceProvider services)
    : ModuleDbContext(options, services, Schema)
{
    /// <summary>The module's tables and its migrations history live in this schema.</summary>
    public const string Schema = "compliance";
}
