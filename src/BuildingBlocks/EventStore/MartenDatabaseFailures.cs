using System.Data.Common;
using System.Runtime.ExceptionServices;
using Marten.Exceptions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;

/// <summary>
/// A database failure as a module's Application expects one, from a read through Marten (#337, docs/adr/0016). Marten
/// 9.59 lets a failure to open the connection through as the NpgsqlException it is, but a failure once its command runs
/// (a dropped connection, a command timeout) goes through its exception transformer, which wraps any NpgsqlException
/// in a <see cref="MartenCommandException"/>: a <see cref="MartenException"/>, no <see cref="DbException"/>. An
/// Application that tells an outage from unreadable data by <see cref="DbException"/>, as the announcement sweeps do,
/// would take it for unreadable data. Anything else Marten throws is left as it is.
/// </summary>
public static class MartenDatabaseFailures
{
    /// <summary>Runs <paramref name="read"/>; a <see cref="MartenCommandException"/> that wraps a <see cref="DbException"/> is rethrown as that exception, with its own stack trace.</summary>
    public static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        Ensure.That(read).IsNotNull();

        try
        {
            return await read();
        }
        catch (MartenCommandException wrapper) when (wrapper.InnerException is DbException failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }
}
