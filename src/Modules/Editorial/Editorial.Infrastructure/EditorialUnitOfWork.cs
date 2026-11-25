using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
        catch (InvalidOperationException)
        {
            // The commit that lost the race already ended the transaction on the server (a failed COMMIT rolls back), and
            // Npgsql then refuses any second end ("This NpgsqlTransaction has completed"); it still names its connection,
            // so that cannot tell (#462). A transaction is open here, so nothing else throws this. Nothing is left to roll
            // back; disposing it lets the context forget it, so the next one can begin.
            await transaction.DisposeAsync();
        }
    }
}
