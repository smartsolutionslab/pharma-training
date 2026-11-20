using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

public sealed class CertificationDbContext(DbContextOptions<CertificationDbContext> options, IServiceProvider services)
    : ModuleDbContext(options, services, Schema)
{
    /// <summary>The module's tables and its migrations history live in this schema.</summary>
    public const string Schema = "certification";
}
