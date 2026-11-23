using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Host.Messaging;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;

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

        // Who may publish which module's events (docs/adr/0011): the broker's permissions limit the exchanges a service's
        // user writes to, not the message-type a message carries, which Wolverine dispatches by. So every message this
        // service sends carries its own user as the AMQP user_id, which RabbitMQ refuses unless it is the connection's,
        // and every event from the broker must belong to the module whose user sent it; the AppHost names every module's
        // user as Messaging:Publishers:<module>.
        var userName = Uri.UnescapeDataString(new Uri(broker).UserInfo.Split(':', 2)[0]);
        var publishers = configuration.GetSection("Messaging:Publishers").GetChildren().ToList();
        if (publishers.Count == 0)
        {
            throw new InvalidOperationException("Messaging:Publishers is missing; the AppHost names every module's broker user there.");
        }

        // A blank user would leave its module without a publisher, and every event of that module would be refused as
        // forged at run time; it fails here instead, naming the module.
        if (publishers.FirstOrDefault(publisher => string.IsNullOrWhiteSpace(publisher.Value)) is { } blank)
        {
            throw new InvalidOperationException($"Messaging:Publishers:{blank.Key} names no broker user; the AppHost names every module's.");
        }

        if (publishers.GroupBy(publisher => publisher.Value, StringComparer.Ordinal).FirstOrDefault(user => user.Count() > 1) is { } shared)
        {
            throw new InvalidOperationException($"The broker user '{shared.Key}' is named for the modules {string.Join(", ", shared.Select(publisher => publisher.Key))}; each module needs a user of its own.");
        }

        services.AddSingleton(new BrokerPublishers(publishers.ToDictionary(publisher => publisher.Value!, publisher => publisher.Key, StringComparer.Ordinal)));

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
            // An event whose publisher may not publish it is refused before any consumer runs, and dead-lettered at once:
            // trying it again cannot change who sent it. This rule comes first, so the retries below never apply to it.
            options.Policies.AddMiddleware(typeof(PublisherCheck));
            // What PublisherEnvelopeMapper refuses as no module's integration event carries a type no handler has; it is
            // dead-lettered rather than discarded, so an operator sees it.
            options.UnknownMessageBehavior = UnknownMessageBehavior.DeadLetterQueue;
            options.OnException<ForgedPublisherException>().MoveToErrorQueue();
            // A failure that says retrying cannot succeed (an exception implementing IPermanentMessageFailure) is
            // dead-lettered on its first attempt. Wolverine takes the first rule that matches, so this one comes before the
            // retries below (docs/adr/0004).
            options.OnException(failure => failure is IPermanentMessageFailure).MoveToErrorQueue();
            // A consumer that throws is tried again, the wait doubling from 1 second, before its message is dead-lettered
            // (seven attempts, about a minute; see docs/adr). The retry is scheduled in the durable inbox, so the
            // listener delivers other messages meanwhile. Each attempt is cancelled after Wolverine's default of 60 seconds,
            // or after the consumer's own [ConsumerTimeout] (UseConsumerTimeouts below; docs/adr/0002).
            options.OnAnyException().ScheduleRetry(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(32));
            // A message given up on is dead-lettered in the wolverine_dead_letters table, where it is replayed from,
            // and not in a dead-letter queue of the broker's (see docs/adr).
            // An event a module of this host both publishes and consumes stays off the broker: no exchange, no sender and no
            // binding to the service's queue, so nothing the broker routes reaches the local consumer (docs/adr/0004). A
            // message of that type that still arrives on the service's queue came from the broker, and PublisherCheck
            // refuses it unless the module's own user sent it (docs/adr/0011, D5). Events of other services are still
            // bound to the queue.
            // Wolverine's request/reply queue (wolverine.response.<id>) is not declared either: its listener dispatches any
            // message by type, the local ones included, and no service asks another for a reply over the broker.
            var local = LocalMessageTypes(modules);
            options.UseRabbitMq(new Uri(broker)).AutoProvision().DisableDeadLetterQueueing().DisableSystemRequestReplyQueueDeclaration()
                .UseConventionalRouting(convention => convention
                    .QueueNameForListener(_ => queue)
                    .ExcludeTypes(local.Contains)
                    .ConfigureListeners((listener, _) => listener.UseInterop((runtime, endpoint) => PublisherMapper(endpoint, runtime, userName)))
                    .ConfigureSending((exchange, _) => exchange.UseInterop((runtime, endpoint) => PublisherMapper(endpoint, runtime, userName))));

            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.CustomizeHandlerDiscovery(query => query.Includes.WithNameSuffix("Consumer"));
            foreach (var module in modules)
            {
                options.Discovery.IncludeAssembly(ModuleAssemblies.Of(module, "Application"));
            }
            options.ResolveDispatcherFromScope()
                .ResolveModuleServicesFromScope(modules)
                .UseConsumerTimeouts();
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

    /// <summary>Wolverine's own mapping of an envelope to RabbitMQ's properties, with the broker user who publishes it.</summary>
    private static PublisherEnvelopeMapper PublisherMapper(Wolverine.Configuration.Endpoint endpoint, Wolverine.Runtime.IWolverineRuntime runtime, string userName) =>
        new PublisherEnvelopeMapper(new RabbitMqEnvelopeMapper(endpoint, runtime), userName);

    /// <summary>The module's name as its assemblies carry it: <c>Organisation</c> of <c>….Modules.Organisation.Api</c>.</summary>
    private static string ModuleNameOf(IModule module)
    {
        var segments = module.GetType().Assembly.GetName().Name!.Split('.');
        return segments[^2];
    }
}
