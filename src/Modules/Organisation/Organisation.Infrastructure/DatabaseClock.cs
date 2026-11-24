using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>The module's database's clock: PostgreSQL's <c>now()</c>, the start of the current transaction.</summary>
public sealed class DatabaseClock(OrganisationDbContext context) : IDatabaseClock
{
    public async Task<Timestamp> NowAsync(CancellationToken cancellationToken)
    {
        var now = await context.Database.SqlQueryRaw<DateTimeOffset>("SELECT now() AS \"Value\"").SingleAsync(cancellationToken);
        return Timestamp.From(now);
    }
}
