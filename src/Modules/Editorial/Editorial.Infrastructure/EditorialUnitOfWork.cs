using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;

/// <summary>
/// The Editorial module's unit of work over its context, the one the request's stores use, whose transaction (the hold of
/// a person, #115) <see cref="RollbackAsync"/> ends.
/// </summary>
public sealed class EditorialUnitOfWork : ModuleUnitOfWork<EditorialDbContext>, IEditorialUnitOfWork
{
    private readonly IServiceProvider services;

    public EditorialUnitOfWork(IServiceProvider services)
        : base(services)
    {
        this.services = services;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<EditorialDbContext>();
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null)
        {
            return;
        }

        try
        {
            await context.Database.RollbackTransactionAsync(cancellationToken);
        }
        catch (InvalidOperationException) when (transaction.GetDbTransaction().Connection is null)
        {
            // The commit that lost the race already ended the transaction on the server (a failed COMMIT rolls back), and
            // the provider then refuses a second end with InvalidOperationException; its connection is gone, which is how
            // ADO.NET says a transaction is no longer valid. Nothing is left to roll back, so this is the rollback done.
        }
    }
}
