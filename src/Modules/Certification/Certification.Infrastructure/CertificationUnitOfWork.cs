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
        var transaction = context.Database.CurrentTransaction;
        if (transaction is not null)
        {
            try
            {
                await context.Database.RollbackTransactionAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // A failed COMMIT already ended the transaction on the server (it rolls back), and Npgsql then refuses any
                // second end ("This NpgsqlTransaction has completed"); it still names its connection, so that cannot tell
                // (#462). A transaction is open here, so nothing else throws this. Disposing it lets the context forget
                // it, so the next person's hold opens a new one.
                await transaction.DisposeAsync();
            }
        }

        context.ChangeTracker.Clear();
    }
}
