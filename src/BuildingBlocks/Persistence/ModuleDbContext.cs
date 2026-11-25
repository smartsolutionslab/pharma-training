using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>The base of every module's DbContext: the module's own schema and its entity configurations.</summary>
public abstract class ModuleDbContext(DbContextOptions options, IServiceProvider services, string schema) : DbContext(options)
{
    /// <summary>The application's services, for conventions that depend on the current request.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>
    /// Value objects and identifiers map to their backing values by convention (module.ddd-depth: strict), and the
    /// row-version marker is checked when the model is built (<see cref="OwnersOnlyRowVersionCheck"/>).
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        Ensure.That(configurationBuilder).IsNotNull();

        configurationBuilder.MapValueObjects(GetType().Assembly);
        configurationBuilder.Conventions.Add(_ => new OwnersOnlyRowVersionCheck());
        base.ConfigureConventions(configurationBuilder);
    }

    /// <summary>
    /// Applies the module's migrations. A module starts with none, and EF Core 11 refuses to migrate a context that has
    /// none (MigrationsNotFound). Without migrations the module gets its database and its migrations history table, as
    /// EF Core 10 gave it; with them, a model that changed since the last one still fails, in MigrateAsync.
    /// </summary>
    public async Task MigrateModuleAsync(CancellationToken cancellationToken = default)
    {
        if (Database.GetMigrations().Any())
        {
            await Database.MigrateAsync(cancellationToken);
            return;
        }

        var database = Database.GetService<IRelationalDatabaseCreator>();
        if (!await database.ExistsAsync(cancellationToken))
        {
            await database.CreateAsync(cancellationToken);
        }

        await Database.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        Ensure.That(optionsBuilder).IsNotNull();

        // Per instance: an interceptor may keep state for the context it belongs to. The bump comes first, so the
        // aggregate roots it marks are part of the save the dispatch sees.
        optionsBuilder.AddInterceptors(
            new RowVersionBump(),
            new SmartSolutionsLab.PharmaTraining.BuildingBlocks.DomainEvents.DomainEventDispatch(Services));
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Ensure.That(modelBuilder).IsNotNull();

        modelBuilder.HasDefaultSchema(schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
