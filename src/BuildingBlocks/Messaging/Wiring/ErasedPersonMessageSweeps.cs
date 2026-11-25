using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace Microsoft.Extensions.DependencyInjection;

public static class ErasedPersonMessageSweeps
{
    /// <summary>
    /// Runs the module's sweep of erased persons' messages (#439, docs/adr/0036): every interval, the dead letters and
    /// handled envelopes of <paramref name="types"/> whose body holds the id of a person in
    /// <c>&lt;erasedPersonsSchema&gt;.erased_persons</c> are deleted. Reads the interval from
    /// <c>&lt;module&gt;:ErasedPersonMessageSweep</c> once, so a bad value or schema stops the host at startup. The
    /// MigrationRunner never adds a module's services, so the sweep never runs there.
    /// </summary>
    public static IServiceCollection AddErasedPersonMessageSweep(
        this IServiceCollection services,
        IConfiguration configuration,
        string module,
        string erasedPersonsSchema,
        IReadOnlyCollection<Type> types)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(module, nameof(module)).IsNotNullOrWhiteSpace();
        Ensure.That(erasedPersonsSchema, nameof(erasedPersonsSchema))
            .IsNotNullOrWhiteSpace()
            .Matches(PersonalMessagePurge.PlainSchema(), "must be a plain lowercase identifier.");
        Ensure.That(types).IsNotNull();
        Ensure.That(types.Count, nameof(types)).AtLeast(1);

        var settings = ErasedPersonMessageSweepSettings.From(configuration, module);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService(provider => new ErasedPersonMessageSweep(
            module,
            erasedPersonsSchema,
            types,
            settings,
            provider.GetRequiredService<IPersonalMessagePurge>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ErasedPersonMessageSweep>>()));
        return services;
    }
}
