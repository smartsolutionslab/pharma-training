using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning.PostgreSql;

namespace Aspire.Hosting;

/// <summary>
/// The one PostgreSQL server every service gets a database of its own on. In run mode it is a container with a data
/// volume, as it always was. Published, it is an Azure Flexible Server, <c>postgres</c>, with password authentication: the
/// deployment sets the administrator and the password, which the Key Vault keeps together with every database's connection
/// string, so a service reads its own as a Key Vault reference. Its size and backups follow the environment
/// (<see cref="DatabaseServerSize"/>), and it has a database for Keycloak beside the modules' (docs/adr/0004).
/// </summary>
public static class Databases
{
    private const string Name = "postgres";

    private const string KeycloakDatabase = "keycloak";

    public static IResourceBuilder<IResourceWithConnectionString> AddDatabaseServer(this IDistributedApplicationBuilder builder, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!secrets.IsPublishing)
        {
            return builder.AddPostgres(Name).WithDataVolume();
        }

        var size = DatabaseServerSize.For(builder.Environment.EnvironmentName);
        var userName = builder.AddParameter($"{Name}-username");
        var password = secrets.Secret($"{Name}-password");
        var server = builder.AddAzurePostgresFlexibleServer(Name)
            .WithPasswordAuthentication(secrets.Vault, userName, password)
            .ConfigureInfrastructure(infrastructure => size.ApplyTo(infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single()));
        server.AddDatabase($"{KeycloakDatabase}-database", KeycloakDatabase);
        return server;
    }

    /// <summary>A module's database on the server, named after the module.</summary>
    public static IResourceBuilder<IResourceWithConnectionString> AddModuleDatabase(this IResourceBuilder<IResourceWithConnectionString> server, string module)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);

        return server switch
        {
            IResourceBuilder<AzurePostgresFlexibleServerResource> flexibleServer => flexibleServer.AddDatabase($"{module}-database", module),
            IResourceBuilder<PostgresServerResource> container => container.AddDatabase($"{module}-database", module),
            _ => throw new InvalidOperationException($"{server.Resource.Name} is not a PostgreSQL server."),
        };
    }
}
