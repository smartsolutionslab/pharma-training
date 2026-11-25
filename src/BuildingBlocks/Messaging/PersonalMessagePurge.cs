using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>The purge over the service's own database.</summary>
public sealed class PersonalMessagePurge(NpgsqlDataSource dataSource) : IPersonalMessagePurge
{
    private readonly NpgsqlDataSource dataSource = dataSource;

    public Task PurgeAsync(Guid personId, IReadOnlyCollection<Type> types, CancellationToken cancellationToken) =>
        throw new NotImplementedException(dataSource.ConnectionString);
}
