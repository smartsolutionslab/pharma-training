using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure;

public sealed class ComplianceUnitOfWork(IServiceProvider services)
    : ModuleUnitOfWork<ComplianceDbContext>(services), IComplianceUnitOfWork;
