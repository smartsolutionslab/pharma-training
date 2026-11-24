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
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddDatabaseErrorDetailGuard(configuration).AddDatabaseErrorDetailGuard(configuration);

        services.Count(service => service.ServiceType == typeof(IHostedService)).ShouldBe(1);
    }

    /// <summary>
    /// A module's own test builds its registrations on logging alone and validates every one (<c>ValidateOnBuild</c>);
    /// the guard asks for nothing a container needs to build it, only for the environment once the host starts.
    /// </summary>
    [Fact]
    public void A_container_with_logging_alone_validates_the_guard()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("ConnectionStrings:database", "Host=db")]).Build();
        var services = new ServiceCollection().AddLogging().AddDatabaseErrorDetailGuard(configuration);

        Should.NotThrow(() => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose());
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

    /// <summary>
    /// The MigrationRunner reads two connection strings, the administrator's (<c>database</c>) and, to ensure the
    /// module's role, the one the service logs in as (<c>application</c>); outside Development it refuses either with
    /// error detail, in both of its modes, before it connects.
    /// </summary>
    [Theory]
    [InlineData("database")]
    [InlineData("application")]
    public void The_migration_runner_refuses_either_of_its_connection_strings_with_error_detail_outside_development(string name)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new($"ConnectionStrings:{name}", "Host=db;Password=" + Password + ";Include Error Detail=true")])
            .Build();

        var refused = Should.Throw<InvalidOperationException>(
            () => DatabaseErrorDetail.RefuseOutsideDevelopment(configuration, Environment(Environments.Production)));

        refused.Message.ShouldContain($"ConnectionStrings:{name}");
        refused.Message.ShouldContain("Include Error Detail");
        refused.Message.ShouldNotContain(Password);
    }

    [Fact]
    public void The_migration_runner_runs_with_neither_connection_string_setting_error_detail_or_in_development()
    {
        var clean = new ConfigurationBuilder()
            .AddInMemoryCollection([new("ConnectionStrings:database", "Host=db"), new("ConnectionStrings:application", "Host=db")])
            .Build();
        var detailed = new ConfigurationBuilder()
            .AddInMemoryCollection([new("ConnectionStrings:application", "Host=db;Include Error Detail=true")])
            .Build();

        Should.NotThrow(() => DatabaseErrorDetail.RefuseOutsideDevelopment(clean, Environment(Environments.Production)));
        Should.NotThrow(() => DatabaseErrorDetail.RefuseOutsideDevelopment(detailed, Environment(Environments.Development)));
    }

    private static ServiceProvider ServicesWith(string? connectionString, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(connectionString is null ? [] : [new("ConnectionStrings:database", connectionString)])
            .Build();
        return new ServiceCollection()
            .AddSingleton<IHostEnvironment>(Environment(environment))
            .AddDatabaseErrorDetailGuard(configuration)
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
