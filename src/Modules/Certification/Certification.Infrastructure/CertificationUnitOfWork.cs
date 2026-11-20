using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

public sealed class CertificationUnitOfWork(IServiceProvider services)
    : ModuleUnitOfWork<CertificationDbContext>(services), ICertificationUnitOfWork;
