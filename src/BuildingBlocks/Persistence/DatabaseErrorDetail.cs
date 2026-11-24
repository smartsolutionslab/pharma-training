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
    private const string ConnectionStringName = "ConnectionStrings:database";

    /// <summary>
    /// Refuses, outside Development, a connection string that sets <c>Include Error Detail</c>, in any spelling Npgsql
    /// reads (it parses it as Npgsql does). The message names the setting, never the connection string, which holds a
    /// password. A missing connection string is not this check's to refuse.
    /// </summary>
    public static void RefuseOutsideDevelopment(string? connectionString, IHostEnvironment environment)
    {
        Ensure.That(environment).IsNotNull();
        if (environment.IsDevelopment() || string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (new NpgsqlConnectionStringBuilder(connectionString).IncludeErrorDetail)
        {
            throw new InvalidOperationException(
                $"{ConnectionStringName} sets Include Error Detail, which puts PostgreSQL's Detail (the values behind a unique violation, "
                + $"the processes of a deadlock) into every logged PostgresException. It is refused outside Development ({environment.EnvironmentName}): "
                + "remove it from the deployment's connection string.");
        }
    }
}
