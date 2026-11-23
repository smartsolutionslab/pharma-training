using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// In publish mode PostgreSQL is one Azure Flexible Server, <c>postgres</c>, with password authentication and its admin
/// password in the Key Vault; a database per service, named after its module, and one for Keycloak. Its size and backups
/// follow the environment: the smallest Burstable size for staging, the smallest General Purpose size for production.
/// </summary>
public class AppHostPostgresTests
{
    private const string ConnectionString = "ConnectionStrings__database";

    [Fact]
    public async Task The_published_model_has_one_flexible_server_with_password_authentication()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var server = builder.Resources.OfType<AzurePostgresFlexibleServerResource>().ShouldHaveSingleItem();

        server.Name.ShouldBe("postgres");
        server.UsePasswordAuthentication.ShouldBeTrue();
        server.Password.ShouldNotBeNull().ValueProviders.OfType<ParameterResource>().Select(parameter => parameter.Name).ShouldBe(["postgres-password"]);
    }

    [Fact]
    public async Task The_server_keeps_its_connection_strings_in_the_key_vault()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var postgres = bicep.Module("postgres");

        postgres.ShouldContain("param secrets_outputs_name string");
        postgres.ShouldContain("'Microsoft.KeyVault/vaults/secrets@");
    }

    [Fact]
    public async Task There_is_a_database_per_module_and_one_for_keycloak()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var modules = PublishedAppHost.Services(builder).Select(service => service.Name).ToList();

        var server = builder.Resources.OfType<AzurePostgresFlexibleServerResource>().ShouldHaveSingleItem();

        modules.ShouldNotBeEmpty();
        server.Databases.Values.Order(StringComparer.Ordinal).ShouldBe(modules.Append("keycloak").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_service_reads_its_own_database()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var services = PublishedAppHost.Services(builder);
        var databases = builder.Resources.OfType<AzurePostgresFlexibleServerDatabaseResource>().ToList();

        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            var configuration = await ExecutionConfigurationBuilder.Create(service)
                .WithEnvironmentVariablesConfig()
                .BuildAsync(builder.ExecutionContext, NullLogger.Instance, TestContext.Current.CancellationToken);
            var environment = configuration.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);
            var database = databases.SingleOrDefault(candidate => candidate.Name == $"{service.Name}-database");

            database.ShouldNotBeNull($"{service.Name} has no database on the Flexible Server.").DatabaseName.ShouldBe(service.Name);
            environment.ShouldContainKey(ConnectionString);
            environment[ConnectionString].ShouldBe(
                $"{{secrets.secrets.connectionstrings--{service.Name}-database-app}}",
                $"{service.Name} should log in to its own database as its module's role, never with the administrator's connection string (ADR 0005).");
        }
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging, "Standard_B2s", "Burstable", 7)]
    [InlineData(PublishedAppHost.Production, "Standard_D2ds_v5", "GeneralPurpose", 35)]
    public async Task The_server_is_sized_and_backed_up_for_its_environment(string environment, string sku, string tier, int retentionDays)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var postgres = bicep.Module("postgres");

        PublishedBicep.Block(postgres, "sku").ShouldContain($"name: '{sku}'");
        PublishedBicep.Block(postgres, "sku").ShouldContain($"tier: '{tier}'");
        PublishedBicep.Block(postgres, "backup").ShouldMatch($@"backupRetentionDays:\s*{retentionDays}\b");
        PublishedBicep.Block(postgres, "backup").ShouldContain("geoRedundantBackup: 'Disabled'");
        PublishedBicep.Block(postgres, "highAvailability").ShouldContain("mode: 'Disabled'");
    }

    [Fact]
    public async Task Run_mode_postgres_allows_the_connections_the_whole_suite_opens()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(1));
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        var postgres = builder.Resources.OfType<PostgresServerResource>().ShouldHaveSingleItem();

        var configuration = await ExecutionConfigurationBuilder.Create(postgres)
            .WithArgumentsConfig()
            .BuildAsync(builder.ExecutionContext, NullLogger.Instance, timeout.Token);
        var arguments = configuration.Arguments.Select(argument => argument.Value).ToList();

        arguments.ShouldContain(
            "max_connections=300",
            "Run mode's PostgreSQL keeps the default of 100 connections, fewer than every service's pools (EF Core, Marten, Wolverine), Keycloak and the integration tests open at once (53300: too many clients).");
        arguments[arguments.IndexOf("max_connections=300") - 1].ShouldBe("-c");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_published_server_keeps_azures_connection_limit(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var postgres = bicep.Module("postgres");

        postgres.ShouldNotContain(
            "max_connections",
            Case.Insensitive,
            "The published Flexible Server keeps the connection limit its size gives it; only run mode's container raises it.");
    }

    [Fact]
    public async Task An_unknown_environment_fails_publishing_naming_the_two_it_knows()
    {
        var failure = await Should.ThrowAsync<Exception>(() => PublishedAppHost.PublishAsync("qa", TestContext.Current.CancellationToken));

        var messages = string.Join(" ", Messages(failure));
        messages.ShouldContain(PublishedAppHost.Staging);
        messages.ShouldContain(PublishedAppHost.Production);
    }

    private static IEnumerable<string> Messages(Exception failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }
}
