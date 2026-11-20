using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure;

public sealed class CatalogUnitOfWork(IServiceProvider services)
    : ModuleUnitOfWork<CatalogDbContext>(services), ICatalogUnitOfWork;
