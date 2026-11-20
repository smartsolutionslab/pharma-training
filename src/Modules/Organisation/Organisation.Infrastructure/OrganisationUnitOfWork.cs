using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

public sealed class OrganisationUnitOfWork(IServiceProvider services)
    : ModuleUnitOfWork<OrganisationDbContext>(services), IOrganisationUnitOfWork;
