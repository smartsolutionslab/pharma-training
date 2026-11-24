using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// Whether a database connection string makes Npgsql put PostgreSQL's Detail into its exceptions. Npgsql redacts the
/// Detail of every <see cref="PostgresException"/> unless the connection string sets <c>Include Error Detail</c>; set, every
/// logged one carries it to the telemetry: the values behind a unique violation (<c>Key (email)=(…)</c>), the processes of
/// a deadlock. Only Development may set it, where it helps debugging and the data is a developer's own.
/// </summary>
public static class DatabaseErrorDetail
{
    private const string Database = "database";

    private const string Application = "application";

    /// <summary>
    /// Refuses, outside Development, the service's <c>ConnectionStrings:database</c> when it sets <c>Include Error Detail</c>,
    /// in any spelling Npgsql reads (it parses it as Npgsql does). The message names the setting, never the connection
    /// string, which holds a password. A missing connection string is not this check's to refuse.
    /// </summary>
    public static void RefuseOutsideDevelopment(string? connectionString, IHostEnvironment environment) =>
        Refuse(Database, connectionString, environment);

    /// <summary>
    /// As above, for both connection strings the MigrationRunner reads: the administrator's (<c>database</c>) and the one
    /// the service logs in as (<c>application</c>), whose role it ensures. Called before it connects, in either mode.
    /// </summary>
    public static void RefuseOutsideDevelopment(IConfiguration configuration, IHostEnvironment environment)
    {
        Ensure.That(configuration).IsNotNull();
        Refuse(Database, configuration.GetConnectionString(Database), environment);
        Refuse(Application, configuration.GetConnectionString(Application), environment);
    }

    private static void Refuse(string name, string? connectionString, IHostEnvironment environment)
    {
        Ensure.That(environment).IsNotNull();
        if (environment.IsDevelopment() || string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (new NpgsqlConnectionStringBuilder(connectionString).IncludeErrorDetail)
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{name} sets Include Error Detail, which puts PostgreSQL's Detail (the values behind a unique violation, "
                + $"the processes of a deadlock) into every logged PostgresException. It is refused outside Development ({environment.EnvironmentName}): "
                + "remove it from the deployment's connection string.");
        }
    }
}
