using Azure.Provisioning.PostgreSql;

namespace Aspire.Hosting;

/// <summary>
/// The Flexible Server's size and backups for each environment the deploy workflow publishes (docs/adr/0004). Staging is
/// the smallest Burstable size that has connections enough for every service's pools and Keycloak (Standard_B1ms allows
/// 50); production the smallest General Purpose size. Neither has high availability or geo-redundant backups.
/// </summary>
public sealed record DatabaseServerSize(string Sku, PostgreSqlFlexibleServerSkuTier Tier, int BackupRetentionDays)
{
    public const string Staging = "staging";

    public const string Production = "production";

    /// <summary>The size for an environment; any other name fails the publish, naming the two it knows.</summary>
    public static DatabaseServerSize For(string environment)
    {
        if (string.Equals(environment, Staging, StringComparison.OrdinalIgnoreCase))
        {
            return new DatabaseServerSize("Standard_B2s", PostgreSqlFlexibleServerSkuTier.Burstable, 7);
        }

        if (string.Equals(environment, Production, StringComparison.OrdinalIgnoreCase))
        {
            return new DatabaseServerSize("Standard_D2ds_v5", PostgreSqlFlexibleServerSkuTier.GeneralPurpose, 35);
        }

        throw new InvalidOperationException($"The AppHost publishes for the environments '{Staging}' and '{Production}' alone, not '{environment}'; pass one of them with -e.");
    }

    /// <summary>Applies the size and backups to the server's Bicep.</summary>
    public void ApplyTo(PostgreSqlFlexibleServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        server.Sku = new PostgreSqlFlexibleServerSku { Name = Sku, Tier = Tier };
        server.Backup = new PostgreSqlFlexibleServerBackupProperties
        {
            BackupRetentionDays = BackupRetentionDays,
            GeoRedundantBackup = PostgreSqlFlexibleServerGeoRedundantBackupEnum.Disabled,
        };
        server.HighAvailability = new PostgreSqlFlexibleServerHighAvailability { Mode = PostgreSqlFlexibleServerHighAvailabilityMode.Disabled };
    }
}
