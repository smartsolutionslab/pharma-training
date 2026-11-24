using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// Refuses to start a service whose database connection string (<c>ConnectionStrings:database</c>, which its DbContexts,
/// its event store and Wolverine's message store all read) sets <c>Include Error Detail</c> outside Development
/// (<see cref="DatabaseErrorDetail"/>). It runs as the host starts, where the environment is known; every module adds it
/// and it is registered once. It is given the connection string as it is registered and the environment as the host
/// starts it, so a container that only validates a module's registrations needs neither.
/// </summary>
public sealed class DatabaseErrorDetailGuard(string? connectionString, IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        DatabaseErrorDetail.RefuseOutsideDevelopment(connectionString, environment);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
