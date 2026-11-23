using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.PostgreSql;

namespace Aspire.Hosting;

/// <summary>
/// The one PostgreSQL server every service gets a database of its own on. In run mode it is a container with a data
/// volume, as it always was. Published, it is an Azure Flexible Server, <c>postgres</c>, with password authentication: the
/// deployment sets the administrator and the password, which the Key Vault keeps together with every database's connection
/// string, so a service reads its own as a Key Vault reference. Its size and backups follow the environment
/// (<see cref="DatabaseServerSize"/>), and it has a database for Keycloak beside the modules' (docs/adr/0005). Published, a
/// service logs in as its module's role, never as the administrator: the server's own module writes the role's connection
/// string into the vault too, <c>connectionstrings--&lt;module&gt;-database-app</c>, for every module's database and no other,
/// with the user <c>&lt;module&gt;_app</c> and the password <c>guid(database_roles_key, '&lt;module&gt;')</c>, derived from
/// the deployment secret <c>database-roles-key</c>. The MigrationRunner, the service's init container, creates the role from
/// it (<see cref="Migrations"/>, OQ8 c and Q1 on #114).
/// </summary>
public static class Databases
{
    private const string Name = "postgres";

    private const string KeycloakDatabase = "keycloak";

    private const string RolesKey = "database-roles-key";

    private const int RolesKeyMinimumLength = 32;

    private const string ServiceVariable = "ConnectionStrings__database";

    // Aspire's name for the vault secret holding a database's connection string, which the role's secret is named after.
    private const string ConnectionStringSecretPrefix = "connectionstrings--";

    private const string RoleSecretSuffix = "-app";

    public static IResourceBuilder<IResourceWithConnectionString> AddDatabaseServer(this IDistributedApplicationBuilder builder, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!secrets.IsPublishing)
        {
            // PostgreSQL's default of 100 connections is fewer than every service's pools (EF Core, Marten, Wolverine), Keycloak
            // and the integration tests open at once (53300: too many clients). Run mode only: the published Flexible Server
            // keeps the limit its size gives it.
            return builder.AddPostgres(Name).WithDataVolume().WithArgs("-c", "max_connections=300");
        }

        var size = DatabaseServerSize.For(builder.Environment.EnvironmentName);
        var userName = builder.AddParameter($"{Name}-username");
        var password = secrets.Secret($"{Name}-password");
        // Every role's password is guid(database_roles_key, '<module>'): the key is all that stands between a guess and every
        // role, so publishing refuses one under 32 characters.
        var rolesKey = secrets.Secret(RolesKey).RequireMinimumLength(RolesKeyMinimumLength);
        var server = builder.AddAzurePostgresFlexibleServer(Name)
            .WithPasswordAuthentication(secrets.Vault, userName, password)
            .WithParameter(Infrastructure.NormalizeBicepIdentifier(RolesKey), rolesKey)
            .ConfigureInfrastructure(infrastructure =>
            {
                var flexibleServer = infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single();
                size.ApplyTo(flexibleServer);
                WriteRoleConnectionStrings(infrastructure, flexibleServer);
            });
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

    /// <summary>
    /// Gives a service its module's database as the connection string <c>database</c>: in run mode Aspire's, as it always
    /// was; published, the module role's from the vault, never the administrator's.
    /// </summary>
    public static IResourceBuilder<T> WithModuleDatabase<T>(this IResourceBuilder<T> service, IResourceBuilder<IResourceWithConnectionString> database, DeploymentSecrets secrets)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(secrets);

        return secrets.IsPublishing
            ? service.WithEnvironment(ServiceVariable, RoleConnectionString(database, secrets))
            : service.WithReference(database, "database");
    }

    /// <summary>
    /// The vault secret holding the connection string a module's role logs in with, which the server's own module writes;
    /// publish mode only.
    /// </summary>
    public static IAzureKeyVaultSecretReference RoleConnectionString(IResourceBuilder<IResourceWithConnectionString> database, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(secrets);

        if (database.Resource is not AzurePostgresFlexibleServerDatabaseResource moduleDatabase)
        {
            throw new InvalidOperationException($"{database.Resource.Name} is no database on the Flexible Server; a module's role exists once published alone.");
        }

        // Written by the server's deployment, so whoever is granted it is granted once the server is deployed.
        var reference = secrets.Vault.Resource.GetSecret(RoleSecretName(moduleDatabase.Name));
        reference.SecretOwner = moduleDatabase.Parent;
        return reference;
    }

    /// <summary>
    /// Writes, next to Aspire's connection strings, one for each module database's role, logging in as
    /// <c>&lt;module&gt;_app</c> with the password derived from <c>database-roles-key</c>. Every database on the server but
    /// Keycloak's is a module's, so nothing is listed per module.
    /// </summary>
    private static void WriteRoleConnectionStrings(AzureResourceInfrastructure infrastructure, PostgreSqlFlexibleServer flexibleServer)
    {
        var server = (AzurePostgresFlexibleServerResource)infrastructure.AspireResource;
        var vault = infrastructure.GetProvisionableResources().OfType<KeyVaultService>().Single();
        var rolesKey = new ProvisioningParameter(Infrastructure.NormalizeBicepIdentifier(RolesKey), typeof(string)) { IsSecure = true };
        infrastructure.Add(rolesKey);

        foreach (var (resourceName, databaseName) in server.Databases.Where(database => database.Value != KeycloakDatabase))
        {
            var role = $"{databaseName.Replace('-', '_')}_app";
            BicepValue<string> salt = databaseName;
            var password = BicepFunction.CreateGuid(rolesKey, salt);
            infrastructure.Add(new KeyVaultSecret(Infrastructure.NormalizeBicepIdentifier($"{resourceName}_app_connectionString"))
            {
                Name = RoleSecretName(resourceName),
                Properties = new SecretProperties
                {
                    Value = BicepFunction.Interpolate($"Host={flexibleServer.FullyQualifiedDomainName};Username={role};Password={password};Database={databaseName}"),
                },
                Parent = vault,
            });
        }
    }

    private static string RoleSecretName(string databaseResource) => $"{ConnectionStringSecretPrefix}{databaseResource}{RoleSecretSuffix}";
}
