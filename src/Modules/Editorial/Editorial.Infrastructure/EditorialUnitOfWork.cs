using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;

public sealed class EditorialUnitOfWork(IServiceProvider services)
    : ModuleUnitOfWork<EditorialDbContext>(services), IEditorialUnitOfWork;
