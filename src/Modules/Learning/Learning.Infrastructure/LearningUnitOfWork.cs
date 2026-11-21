using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure;

public sealed class LearningUnitOfWork(IServiceProvider services)
    : ModuleUnitOfWork<LearningDbContext>(services), ILearningUnitOfWork;
