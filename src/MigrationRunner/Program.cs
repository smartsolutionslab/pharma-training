using System.Reflection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

// Applies one module's migrations to its service's own database, then exits. In run mode the AppHost runs this once per
// service, naming the module in Migrations:Module, and starts the service only after its run completes; published, it is
// each service's init container (docs/adr/0005). It logs in as the administrator (ConnectionStrings:database). Given the
// connection string the service logs in with (ConnectionStrings:application), it then ensures that module's role.
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

var module = builder.Configuration["Migrations:Module"]
    ?? throw new InvalidOperationException("Migrations:Module is missing; the AppHost names the module whose database this run migrates.");
var administrator = builder.Configuration.GetConnectionString("database")
    ?? throw new InvalidOperationException("Connection string 'database' is missing; the AppHost gives the administrator's for the module's database.");
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
await using (await MigrationGate.EnterAsync(administrator, CancellationToken.None))
{
    foreach (var context in contexts)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await ((ModuleDbContext)scope.ServiceProvider.GetRequiredService(context)).MigrateModuleAsync();
    }

    await host.Services.ApplyEventStoreSchemasAsync(infrastructure);

    if (!string.IsNullOrWhiteSpace(application))
    {
        await ModuleDatabaseRole.EnsureAsync(administrator, application, CancellationToken.None);
    }
}

// The module's namespace, as fabric writes it from the module's name: order-handling is OrderHandling.
static string Namespace(string module) =>
    string.Concat(module.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
