using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence.Tests;

/// <summary>
/// Npgsql leaves PostgreSQL's Detail out of a <c>PostgresException</c> unless the connection string sets
/// <c>Include Error Detail</c>, and every logged one carries it then: the values behind a unique violation, the processes of
/// a deadlock. A service host refuses to start with it outside Development, and the MigrationRunner refuses to run, so a
/// connection string set that way while debugging never sends those values to the telemetry.
/// </summary>
public class DatabaseErrorDetailGuardTests
{
    private const string Password = "s3cret-password";

    [Theory]
    [InlineData("Host=db;Database=organisation;Username=app;Password=" + Password + ";Include Error Detail=true")]
    [InlineData("Host=db;Database=organisation;Username=app;Password=" + Password + ";IncludeErrorDetail=True")]
    [InlineData("Host=db;Database=organisation;Username=app;Password=" + Password + ";include error detail = true")]
    public async Task A_service_refuses_to_start_outside_development_when_its_database_includes_error_detail(string connectionString)
    {
        await using var services = ServicesWith(connectionString, Environments.Production);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(services));

        refused.Message.ShouldContain("Include Error Detail");
        refused.Message.ShouldContain("ConnectionStrings:database");
        refused.Message.ShouldNotContain(Password);
    }

    [Fact]
    public async Task A_service_starts_in_development_with_error_detail_included()
    {
        await using var services = ServicesWith("Host=db;Include Error Detail=true", Environments.Development);

        await Should.NotThrowAsync(() => StartAsync(services));
    }

    [Theory]
    [InlineData("Host=db;Database=organisation;Username=app;Password=" + Password)]
    [InlineData("Host=db;Include Error Detail=false")]
    public async Task A_service_starts_outside_development_when_its_database_leaves_error_detail_out(string connectionString)
    {
        await using var services = ServicesWith(connectionString, Environments.Production);

        await Should.NotThrowAsync(() => StartAsync(services));
    }

    [Fact]
    public async Task A_service_without_a_database_starts()
    {
        await using var services = ServicesWith(connectionString: null, Environments.Production);

        await Should.NotThrowAsync(() => StartAsync(services));
    }

    [Fact]
    public void The_guard_is_registered_once_however_many_modules_add_it()
    {
        var services = new ServiceCollection().AddDatabaseErrorDetailGuard().AddDatabaseErrorDetailGuard();

        services.Count(service => service.ImplementationType == typeof(DatabaseErrorDetailGuard)).ShouldBe(1);
    }

    [Fact]
    public void The_migration_runner_refuses_outside_development_a_database_that_includes_error_detail()
    {
        var refused = Should.Throw<InvalidOperationException>(
            () => DatabaseErrorDetail.RefuseOutsideDevelopment("Host=db;Password=" + Password + ";Include Error Detail=true", Environment(Environments.Staging)));

        refused.Message.ShouldContain("Include Error Detail");
        refused.Message.ShouldNotContain(Password);
    }

    [Fact]
    public void The_migration_runner_runs_in_development_with_error_detail_included()
    {
        Should.NotThrow(() => DatabaseErrorDetail.RefuseOutsideDevelopment("Host=db;Include Error Detail=true", Environment(Environments.Development)));
    }

    private static ServiceProvider ServicesWith(string? connectionString, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(connectionString is null ? [] : [new("ConnectionStrings:database", connectionString)])
            .Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IHostEnvironment>(Environment(environment))
            .AddDatabaseErrorDetailGuard()
            .BuildServiceProvider();
    }

    private static HostingEnvironment Environment(string name) => new() { EnvironmentName = name };

    private static async Task StartAsync(ServiceProvider services)
    {
        foreach (var hosted in services.GetServices<IHostedService>())
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        }
    }
}
