using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

/// <summary>
/// The Certification module's unit of work over its context, the one the request's stores use, whose transaction (the
/// hold of a person, #115) <see cref="AbandonAsync"/> rolls back before it forgets what the context tracks.
/// </summary>
public sealed class CertificationUnitOfWork : ModuleUnitOfWork<CertificationDbContext>, ICertificationUnitOfWork
{
    private readonly IServiceProvider services;

    public CertificationUnitOfWork(IServiceProvider services)
        : base(services)
    {
        this.services = services;
    }

    public async Task AbandonAsync(CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<CertificationDbContext>();
        if (context.Database.CurrentTransaction is not null)
        {
            await context.Database.RollbackTransactionAsync(cancellationToken);
        }

        context.ChangeTracker.Clear();
    }
}
