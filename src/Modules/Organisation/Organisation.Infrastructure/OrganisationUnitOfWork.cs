using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// The Organisation module's unit of work over its context, the one the request's stores use, whose transaction
/// <see cref="RollbackAsync"/> ends.
/// </summary>
public sealed class OrganisationUnitOfWork(IServiceProvider services, OrganisationDbContext context)
    : ModuleUnitOfWork<OrganisationDbContext>(services), IOrganisationUnitOfWork
{
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            await context.Database.RollbackTransactionAsync(cancellationToken);
        }
    }
}
