using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

public static class PeriodicCommands
{
    /// <summary>
    /// Runs <typeparamref name="TCommand"/>, a command without data, every <paramref name="interval"/> through a
    /// <see cref="PeriodicCommandService{TCommand}"/> on the container's clock, the system's where the container has none
    /// (#115, slice 5: Certification's purge of erased persons' certificates). The module reads and checks the interval
    /// itself, so a bad value stops the host at startup. The MigrationRunner never adds a module's services, so the
    /// command never runs there.
    /// </summary>
    public static IServiceCollection AddPeriodicCommand<TCommand>(this IServiceCollection services, TimeSpan interval)
        where TCommand : ICommand<Result<Unit, ApiError>>, new()
    {
        Ensure.That(services).IsNotNull();

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService(provider => new PeriodicCommandService<TCommand>(
            interval,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PeriodicCommandService<TCommand>>>()));
        return services;
    }
}
