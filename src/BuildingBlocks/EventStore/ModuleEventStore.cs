using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;

/// <summary>
/// One module's Marten store, found from its Infrastructure assembly: its own schema of the project's database (the one
/// its DbContext uses), its domain events, the projections of its Infrastructure project, run inline, and a partial index
/// on the stored time of each event type an adapter declares as <c>SweptEventType</c> (#277). The host and
/// the MigrationRunner configure the store the same way; only the runner changes the schema.
/// </summary>
public sealed class ModuleEventStore
{
    private const string ConnectionStringName = "database";

    // <Module>AggregateStore.Schema, rendered from the module's name as its DbContext's schema is.
    private const string SchemaConstant = "Schema";

    // Marten's stored name of the events an adapter sweeps for (#277), declared by the finder of the module's
    // announcement sweep: each gets a partial index on its stored time (SweptEventIndex).
    private const string SweptEventTypeConstant = "SweptEventType";

    private readonly string schema;
    private readonly IReadOnlyList<Type> domainEvents;
    private readonly IReadOnlyList<Type> projections;

    private ModuleEventStore(string schema, IReadOnlyList<Type> adapters, IReadOnlyList<Type> domainEvents, IReadOnlyList<Type> projections, IReadOnlyList<string> sweptEventTypes)
    {
        this.schema = schema;
        Adapters = adapters;
        this.domainEvents = domainEvents;
        this.projections = projections;
        SweptEventIndexes = [.. sweptEventTypes.Select(sweptEventType => new SweptEventIndex(schema, sweptEventType))];
    }

    /// <summary>
    /// The module's classes over its store: those with a constructor taking an <see cref="IDocumentStore"/>, whatever
    /// else it takes (the container supplies the rest).
    /// </summary>
    public IReadOnlyList<Type> Adapters { get; }

    /// <summary>The partial index on the stored time of each event type an adapter sweeps for (#277).</summary>
    public IReadOnlyList<SweptEventIndex> SweptEventIndexes { get; }

    /// <summary>The module's event store, or null when its Infrastructure project has no adapter over one.</summary>
    public static ModuleEventStore? Of(Assembly infrastructure)
    {
        Ensure.That(infrastructure).IsNotNull();

        var types = infrastructure.GetTypes();
        var adapters = types
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.GetConstructors().Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IDocumentStore))))
            .ToList();
        if (adapters.Count == 0)
        {
            return null;
        }

        // <Namespace>.Modules.<Module>.Infrastructure: the module's Domain assembly follows from the name.
        var name = infrastructure.GetName().Name!;
        var domain = Assembly.Load(name[..name.LastIndexOf('.')] + ".Domain");
        var schema = adapters
            .Select(adapter => adapter.GetField(SchemaConstant, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue())
            .OfType<string>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"{name} has no adapter declaring the module's schema ({SchemaConstant}).");
        var domainEvents = domain.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
            .ToList();
        var projections = types
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IProjectionSource<IDocumentOperations, IQuerySession>).IsAssignableFrom(type))
            .ToList();

        var sweptEventTypes = adapters
            .Select(adapter => adapter.GetField(SweptEventTypeConstant, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue())
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ModuleEventStore(schema, adapters, domainEvents, projections, sweptEventTypes);
    }

    public static string ConnectionString(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        return configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing; Aspire provides it.");
    }

    /// <param name="schemaChanges">AutoCreate.None in the application; the MigrationRunner passes CreateOrUpdate.</param>
    public void Configure(StoreOptions options, string connectionString, AutoCreate schemaChanges)
    {
        Ensure.That(options).IsNotNull();

        options.Connection(connectionString);
        options.DatabaseSchemaName = schema;
        options.Events.DatabaseSchemaName = schema;
        options.UseSystemTextJsonForSerialization(configure: json =>
        {
            // A null for a value object that is not nullable, or a constructor member missing from the JSON, fails instead of skipping From.
            json.RespectNullableAnnotations = true;
            json.RespectRequiredConstructorParameters = true;
            json.Converters.Add(new ValueObjectJsonConverterFactory());
        });
        options.AutoCreateSchemaObjects = schemaChanges;
        options.Events.AddEventTypes(domainEvents);
        foreach (var index in SweptEventIndexes)
        {
            // Not Marten's own: the runner makes it after the event tables, and Marten's migrations must not drop it.
            options.Events.IgnoreIndex(index.Name);
        }

        foreach (var projection in projections)
        {
            options.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)Activator.CreateInstance(projection)!, ProjectionLifecycle.Inline);
        }
    }
}
