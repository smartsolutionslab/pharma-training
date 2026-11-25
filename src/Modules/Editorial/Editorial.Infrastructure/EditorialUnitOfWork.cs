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
        if (context.Database.CurrentTransaction is not null)
        {
            await context.Database.RollbackTransactionAsync(cancellationToken);
        }
    }
}
