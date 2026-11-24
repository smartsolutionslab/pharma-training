using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;

/// <summary>
/// How long each lock the MigrationRunner's build and drop of a <see cref="SweptEventIndex"/> wait for may keep them waiting
/// (#351): the transactions that write to <c>mt_events</c>, and every transaction holding a snapshot older than the build's,
/// a session idle in one, a <c>pg_dump</c> or a long report included. Unbounded, such a session would hold the runner, and
/// with it the new revision, for good, without a word; bounded, the run fails on the lock timeout and the next run builds
/// again. The build itself has no time limit. It is the runner's setting <c>Migrations:IndexLockTimeout</c> (#359), read
/// once as the run starts: a blank or absent value takes 30 s; a value that does not parse in the invariant <c>c</c>
/// format (<c>[d.]hh:mm:ss[.fff]</c>) or lies outside one second to one hour fails the run, naming the key and the value.
/// </summary>
public sealed record IndexLockTimeout
{
    public const string Setting = "Migrations:IndexLockTimeout";

    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    /// <summary>PostgreSQL reads a <c>lock_timeout</c> of zero as no bound, so nothing near it is passed on.</summary>
    public static readonly TimeSpan Least = TimeSpan.FromSeconds(1);

    /// <summary>Longer than any rollout waits for an init container.</summary>
    public static readonly TimeSpan Most = TimeSpan.FromHours(1);

    private IndexLockTimeout(TimeSpan value) => Value = value;

    public TimeSpan Value { get; }

    /// <summary>Bounds the lock waits of the session it runs in by <see cref="Value"/>.</summary>
    public string Statement => $"set lock_timeout = '{(long)Value.TotalMilliseconds}ms'";

    /// <summary>The runner's setting, from <c>Migrations:IndexLockTimeout</c>.</summary>
    public static IndexLockTimeout From(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        var value = configuration[Setting];
        if (string.IsNullOrWhiteSpace(value))
        {
            return new IndexLockTimeout(Default);
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var lockTimeout) || lockTimeout < Least || lockTimeout > Most)
        {
            throw new InvalidOperationException(
                $"The setting {Setting} is '{value}'; it accepts a duration from {Render(Least)} to {Render(Most)}, or nothing for {Render(Default)}.");
        }

        return new IndexLockTimeout(lockTimeout);
    }

    private static string Render(TimeSpan duration) => duration.ToString("c", CultureInfo.InvariantCulture);
}
