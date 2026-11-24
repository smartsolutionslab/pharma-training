using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// Refuses to start a service whose database connection string (<c>ConnectionStrings:database</c>, which its DbContexts,
/// its event store and Wolverine's message store all read) sets <c>Include Error Detail</c> outside Development
/// (<see cref="DatabaseErrorDetail"/>). It runs as the host starts, where the environment is known; every module adds it
/// and it is registered once.
/// </summary>
public sealed class DatabaseErrorDetailGuard(IConfiguration configuration, IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        DatabaseErrorDetail.RefuseOutsideDevelopment(configuration.GetConnectionString("database"), environment);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
