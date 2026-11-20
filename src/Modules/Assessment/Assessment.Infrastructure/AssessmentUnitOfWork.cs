using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure;

public sealed class AssessmentUnitOfWork(IServiceProvider services)
    : ModuleUnitOfWork<AssessmentDbContext>(services), IAssessmentUnitOfWork;
