using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

// Applies one module's migrations to its service's own database, then exits. In run mode the AppHost runs this once per
// service, naming the module in Migrations:Module, and starts the service only after its run completes; published, it is
// each service's init container (docs/adr/0005). It logs in as the administrator (ConnectionStrings:database). Given the
// connection string the service logs in with (ConnectionStrings:application), it then ensures that module's role. It
// builds Wolverine's message storage too, as the administrator, so the service never has to; it needs no broker for it.
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

// Published, Keycloak's init container names no module but the schema its role owns (Migrations:OwnedSchema): it ensures
// that role and schema alone, and nothing below runs (MigrationRun). A run naming neither fails here.
using (var loggers = LoggerFactory.Create(logging => logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddSimpleConsole()))
{
    if (await MigrationRun.RunOwnedSchemaModeAsync(builder.Configuration, loggers.CreateLogger("MigrationRunner"), CancellationToken.None))
    {
        return;
    }
}

var module = builder.Configuration[MigrationRun.ModuleSetting]
    ?? throw new InvalidOperationException("Migrations:Module is missing; the AppHost names the module whose database this run migrates.");
// How long the swept event indexes' build and drop may wait for each lock (#359): read now, so a bad value fails the run
// before it migrates anything.
var indexLockTimeout = IndexLockTimeout.From(builder.Configuration);
// Every session the runner opens as the administrator, EF Core's and Marten's too (both read ConnectionStrings:database),
// resolves unqualified names in pg_catalog alone: defence in depth, should a role ever hold CREATE on the database and plant
// code on the administrator's search_path that these sessions would run with the administrator's rights (CVE-2018-1058,
// AdministratorSession). What they make names the module's schema explicitly.
var database = builder.Configuration.GetConnectionString("database")
    ?? throw new InvalidOperationException("Connection string 'database' is missing; the AppHost gives the administrator's for the module's database.");
// The runner never starts its host, so it checks here what a service's host checks as it starts (DatabaseErrorDetailGuard).
DatabaseErrorDetail.RefuseOutsideDevelopment(database, builder.Environment);
var administrator = AdministratorSession.Pinned(database);
builder.Configuration.AddInMemoryCollection([new("ConnectionStrings:database", administrator)]);
var application = builder.Configuration.GetConnectionString("application");
var infrastructure = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"SmartSolutionsLab.PharmaTraining.Modules.{Namespace(module)}.Infrastructure.dll"));
var contexts = infrastructure.GetTypes()
    .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ModuleDbContext).IsAssignableFrom(type))
    .ToList();

foreach (var context in contexts)
{
    builder.Services.AddModuleDbContext(context, builder.Configuration);
}

using var host = builder.Build();
// Every replica's init container runs this; one at a time per database.
await using (await MigrationGate.EnterAsync(administrator, host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MigrationGate"), host.Services.GetRequiredService<TimeProvider>(), CancellationToken.None))
{
    // Published, where a module role exists: the schema named after the administrator is the administrator's before the
    // role could take it, so no other administrator session (an operator's psql) runs what a role planted there either.
    if (!string.IsNullOrWhiteSpace(application))
    {
        await AdministratorSchema.ClaimAsync(administrator, CancellationToken.None);
    }

    foreach (var context in contexts)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await ((ModuleDbContext)scope.ServiceProvider.GetRequiredService(context)).MigrateModuleAsync();
    }

    await host.Services.ApplyEventStoreSchemasAsync(infrastructure, indexLockTimeout);

    // Wolverine's inbox, outbox and dead letters, with the service hosts' own configuration, owned by the administrator;
    // the role's grants below cover it like every schema the administrator owns. A schema of that name some other role
    // owns, or holds an object of, fails the run, as does a trigger, rule, policy or SECURITY DEFINER function there that
    // Wolverine does not make.
    await ModuleMessageStorage.BuildAsync(administrator, CancellationToken.None);

    // After every migration of the run, so the role's grants cover what they made and it never keeps write access to a
    // migration history table they made. The run fails, naming the object and the role, if the role owns anything in the
    // database, and takes back any CREATE it holds on a schema.
    if (!string.IsNullOrWhiteSpace(application))
    {
        await ModuleDatabaseRole.EnsureAsync(administrator, application, host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MigrationRunner"), CancellationToken.None);
    }
}

// The module's namespace, as fabric writes it from the module's name: order-handling is OrderHandling.
static string Namespace(string module) =>
    string.Concat(module.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
