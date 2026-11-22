using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Contracts;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace Microsoft.Extensions.DependencyInjection;

public static class Messaging
{
    private static readonly HashSet<string> HandlerMethodNames = new(["Handle", "HandleAsync", "Consume", "ConsumeAsync"], StringComparer.Ordinal);

    /// <summary>Wolverine with a durable outbox and inbox in the project's database, the broker as transport, and every module's consumers.</summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration, IReadOnlyList<IModule> modules)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(modules).IsNotNull();

        var database = configuration.GetConnectionString("database")
            ?? throw new InvalidOperationException("Connection string 'database' is missing; Aspire provides it.");
        var broker = configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("Connection string 'messaging' is missing; Aspire provides it.");

        // Every service listens on a queue of its own, which the AppHost names after its module and binds to the exchange of
        // each event the service consumes: services that shared the queue named after the event would compete for it, and
        // an event would reach only one of them (see docs/adr).
        var queue = configuration["Messaging:Queue"]
            ?? throw new InvalidOperationException("Messaging:Queue is missing; the AppHost names the service's queue after its module.");

        services.AddWolverine(options =>
        {
            // Handler code is generated at startup. For production, pre-generate it (see docs/adr).
            options.UseRuntimeCompilation();
            options.PersistMessagesWithPostgresql(database, "wolverine");
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            options.Policies.UseDurableInboxOnAllListeners();
            // An event a module of this host consumes goes to a local queue, not the broker (see docs/adr); durable, it is
            // stored with the publishing module's changes and its retries are scheduled in the inbox, so a restart keeps it.
            options.Policies.UseDurableLocalQueues();
            // A consumer that throws is tried again, the wait doubling from 1 second, before its message is dead-lettered
            // (seven attempts, about a minute; see docs/adr). The retry is scheduled in the durable inbox, so the
            // listener delivers other messages meanwhile.
            options.OnAnyException().ScheduleRetry(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(32));
            // A message given up on is dead-lettered in the wolverine_dead_letters table, where it is replayed from,
            // and not in a dead-letter queue of the broker's (see docs/adr).
            // An event a module of this host both publishes and consumes stays off the broker altogether: no exchange, no
            // sender and no binding to the service's queue, so it travels on the local queue alone and nobody with broker
            // credentials can hand the host one (docs/adr/0004). Events of other services are still bound to the queue.
            var local = LocalMessageTypes(modules);
            options.UseRabbitMq(new Uri(broker)).AutoProvision().DisableDeadLetterQueueing().UseConventionalRouting(convention => convention
                .QueueNameForListener(_ => queue)
                .ExcludeTypes(local.Contains));

            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.CustomizeHandlerDiscovery(query => query.Includes.WithNameSuffix("Consumer"));
            foreach (var module in modules)
            {
                options.Discovery.IncludeAssembly(ModuleAssemblies.Of(module, "Application"));
            }
            options.ResolveDispatcherFromScope()
                .ResolveModuleServicesFromScope(modules);
        });

        return services;
    }

    /// <summary>
    /// The message types a module of this host both publishes and consumes: those its modules' <c>*Consumer</c> classes
    /// handle (the handlers Wolverine discovers below) whose contract lives in the Contracts namespace of one of the
    /// host's modules (<c>src/Contracts/&lt;Module&gt;/</c>), which is where a module's integration events are declared.
    /// </summary>
    private static HashSet<Type> LocalMessageTypes(IReadOnlyList<IModule> modules)
    {
        var contracts = modules
            .Select(module => $"{typeof(IntegrationEvent).Namespace}.{ModuleNameOf(module)}")
            .ToHashSet(StringComparer.Ordinal);
        return [.. modules
            .SelectMany(module => ModuleAssemblies.Of(module, "Application").GetExportedTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Consumer", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => HandlerMethodNames.Contains(method.Name) && method.GetParameters().Length > 0)
            .Select(method => method.GetParameters()[0].ParameterType)
            .Where(message => message.Namespace is { } name && contracts.Contains(name))];
    }

    /// <summary>The module's name as its assemblies carry it: <c>Organisation</c> of <c>….Modules.Organisation.Api</c>.</summary>
    private static string ModuleNameOf(IModule module)
    {
        var segments = module.GetType().Assembly.GetName().Name!.Split('.');
        return segments[^2];
    }
}
