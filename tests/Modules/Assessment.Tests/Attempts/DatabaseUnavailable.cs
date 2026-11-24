using System.Data.Common;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>What a store throws when the database cannot be reached: an outage, never data that cannot be read.</summary>
public sealed class DatabaseUnavailable : DbException
{
    public DatabaseUnavailable()
        : base("The database cannot be reached.")
    {
    }

    public DatabaseUnavailable(string message)
        : base(message)
    {
    }

    public DatabaseUnavailable(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
