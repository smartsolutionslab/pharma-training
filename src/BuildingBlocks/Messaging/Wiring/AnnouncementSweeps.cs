using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

public static class AnnouncementSweeps
{
    /// <summary>
    /// Runs the module's announcement sweep (#277, docs/adr/0016): <typeparamref name="TCommand"/>, sent every interval
    /// by a <see cref="PeriodicCommandService{TCommand}"/>, announces every stored event whose announcement never left.
    /// Reads <see cref="AnnouncementSweepSettings"/> from <c>&lt;module&gt;:AnnouncementSweep</c> once, so a bad value
    /// stops the host at startup, and registers them for the command's handler. The MigrationRunner never adds a
    /// module's services, so the sweep never runs there.
    /// </summary>
    public static IServiceCollection AddAnnouncementSweep<TCommand>(this IServiceCollection services, IConfiguration configuration, string module)
        where TCommand : ICommand<Result<Unit, ApiError>>, new()
    {
        Ensure.That(services).IsNotNull();

        var settings = AnnouncementSweepSettings.From(configuration, module);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(settings);
        services.AddHostedService(provider => new PeriodicCommandService<TCommand>(
            settings.Interval,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PeriodicCommandService<TCommand>>>()));
        return services;
    }
}
