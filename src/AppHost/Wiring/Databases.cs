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
/// string into the vault too, <c>connectionstrings--&lt;database&gt;-database-app</c>, for every database on the server,
/// Keycloak's included, with the user <c>&lt;database&gt;_app</c> and the password
/// <c>guid(database_roles_key, '&lt;database&gt;')</c>, derived from the deployment secret <c>database-roles-key</c>. The
/// MigrationRunner, the service's init container, creates the role from it (<see cref="Migrations"/>, OQ8 c and Q1 on #114).
/// Keycloak, which takes its password alone, reads it from <c>keycloak-database-password</c>, which the server's module
/// writes too; its init container ensures <c>keycloak_app</c> (Q2 on #114).
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

    private const string PasswordSecretSuffix = "-password";

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
            throw new InvalidOperationException($"{database.Resource.Name} is no database on the Flexible Server; a database's role exists once published alone.");
        }

        // Written by the server's deployment, so whoever is granted it is granted once the server is deployed.
        var reference = secrets.Vault.Resource.GetSecret(RoleSecretName(moduleDatabase.Name));
        reference.SecretOwner = moduleDatabase.Parent;
        return reference;
    }

    /// <summary>
    /// Keycloak's database on the server; publish mode only, where Keycloak keeps its data there as its own role
    /// (<see cref="KeycloakPublishing"/>).
    /// </summary>
    public static IResourceBuilder<IResourceWithConnectionString> KeycloakDatabaseOf(IResourceBuilder<IResourceWithConnectionString> server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var database = server.ApplicationBuilder.Resources.OfType<AzurePostgresFlexibleServerDatabaseResource>()
            .SingleOrDefault(candidate => candidate.Parent == server.Resource && candidate.DatabaseName == KeycloakDatabase)
            ?? throw new InvalidOperationException($"{server.Resource.Name} has no database {KeycloakDatabase}; it exists once published alone.");
        return server.ApplicationBuilder.CreateResourceBuilder<IResourceWithConnectionString>(database);
    }

    /// <summary>
    /// The vault secret holding the password of a database's role alone, which the server's own module writes for
    /// Keycloak's database (<c>keycloak-database-password</c>); publish mode only.
    /// </summary>
    public static IAzureKeyVaultSecretReference RolePassword(IResourceBuilder<IResourceWithConnectionString> database, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(secrets);

        if (database.Resource is not AzurePostgresFlexibleServerDatabaseResource keycloakDatabase || keycloakDatabase.DatabaseName != KeycloakDatabase)
        {
            throw new InvalidOperationException($"{database.Resource.Name} is not Keycloak's database on the Flexible Server; only its role's password is written alone.");
        }

        var reference = secrets.Vault.Resource.GetSecret($"{keycloakDatabase.Name}{PasswordSecretSuffix}");
        reference.SecretOwner = keycloakDatabase.Parent;
        return reference;
    }

    /// <summary>
    /// Writes, next to Aspire's connection strings, one for each database's role, logging in as
    /// <c>&lt;database&gt;_app</c> with the password derived from <c>database-roles-key</c>, and for Keycloak's, which takes
    /// the password alone, that password too. Nothing is listed per module.
    /// </summary>
    private static void WriteRoleConnectionStrings(AzureResourceInfrastructure infrastructure, PostgreSqlFlexibleServer flexibleServer)
    {
        var server = (AzurePostgresFlexibleServerResource)infrastructure.AspireResource;
        var vault = infrastructure.GetProvisionableResources().OfType<KeyVaultService>().Single();
        var rolesKey = new ProvisioningParameter(Infrastructure.NormalizeBicepIdentifier(RolesKey), typeof(string)) { IsSecure = true };
        infrastructure.Add(rolesKey);

        foreach (var (resourceName, databaseName) in server.Databases)
        {
            var role = RoleName(databaseName);
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

            if (databaseName == KeycloakDatabase)
            {
                infrastructure.Add(new KeyVaultSecret(Infrastructure.NormalizeBicepIdentifier($"{resourceName}_password"))
                {
                    Name = $"{resourceName}{PasswordSecretSuffix}",
                    Properties = new SecretProperties { Value = password },
                    Parent = vault,
                });
            }
        }
    }

    /// <summary>The role a database's app logs in as once published: <c>&lt;database&gt;_app</c>.</summary>
    public static string RoleName(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        return $"{databaseName.Replace('-', '_')}_app";
    }

    private static string RoleSecretName(string databaseResource) => $"{ConnectionStringSecretPrefix}{databaseResource}{RoleSecretSuffix}";
}
