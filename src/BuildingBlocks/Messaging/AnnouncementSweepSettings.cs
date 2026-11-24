using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// How a module's announcement sweep (#277, docs/adr/0016) runs, read once at startup from the module's own section,
/// <c>&lt;Module&gt;:AnnouncementSweep</c>. A blank or absent value takes the default; a value that does not parse or lies
/// outside its range stops the host, naming the key, the value and what it accepts. Durations are read in the invariant
/// <c>c</c> format (<c>[d.]hh:mm:ss</c>), the batch size as a whole number.
/// </summary>
public sealed record AnnouncementSweepSettings
{
    public const string Section = "AnnouncementSweep";

    public const string IntervalKey = "Interval";

    public const string GracePeriodKey = "GracePeriod";

    public const string LookbackKey = "Lookback";

    public const string BatchSizeKey = "BatchSize";

    public const int DefaultBatchSize = 100;

    public const int LeastBatchSize = 1;

    public const int MostBatchSize = 1000;

    /// <summary>
    /// The most pages of <see cref="BatchSize"/> events one pass reads: it reads on past events it cannot read or map,
    /// so that a batch of them holds up no newer event, but never reads more than this many batches' worth in one pass.
    /// </summary>
    public const int MostPagesPerPass = 10;

    /// <summary>
    /// The most events one pass reads, whatever its <see cref="BatchSize"/> (#351): with the largest batch, ten pages of
    /// events that cannot be read would otherwise cost every replica ten thousand loads and warnings a pass. It is
    /// <see cref="MostPagesPerPass"/> pages of the default batch, so a pass at the default reads as far as it did.
    /// </summary>
    public const int MostEventsPerPass = MostPagesPerPass * DefaultBatchSize;

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(7);

    private static readonly TimeSpan LeastInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MostInterval = TimeSpan.FromHours(1);

    private static readonly TimeSpan LeastLookback = TimeSpan.FromHours(1);

    private static readonly TimeSpan MostLookback = TimeSpan.FromDays(90);

    private AnnouncementSweepSettings(TimeSpan interval, TimeSpan gracePeriod, TimeSpan lookback, int batchSize)
    {
        Interval = interval;
        GracePeriod = gracePeriod;
        Lookback = lookback;
        BatchSize = batchSize;
    }

    /// <summary>The time between two passes.</summary>
    public TimeSpan Interval { get; }

    /// <summary>An event stored more recently than this is left to the handler that stored it.</summary>
    public TimeSpan GracePeriod { get; }

    /// <summary>An event stored longer ago than this is never looked at again.</summary>
    public TimeSpan Lookback { get; }

    /// <summary>The most events one pass announces; the rest wait for the next pass.</summary>
    public int BatchSize { get; }

    /// <summary>The module's settings, from <c>&lt;module&gt;:AnnouncementSweep</c>.</summary>
    public static AnnouncementSweepSettings From(IConfiguration configuration, string module)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(module).IsNotNullOrWhiteSpace();

        var section = $"{module}:{Section}";
        var interval = DurationAt(configuration, $"{section}:{IntervalKey}", DefaultInterval, LeastInterval, MostInterval);
        var gracePeriod = DurationAt(configuration, $"{section}:{GracePeriodKey}", DefaultGracePeriod, LeastInterval, MostInterval);
        var lookback = DurationAt(configuration, $"{section}:{LookbackKey}", DefaultLookback, LeastLookback, MostLookback);
        var batchSize = CountAt(configuration, $"{section}:{BatchSizeKey}");
        if (lookback <= gracePeriod)
        {
            throw new InvalidOperationException(
                $"The setting {section}:{LookbackKey} ({Render(lookback)}) must be longer than {section}:{GracePeriodKey} ({Render(gracePeriod)}).");
        }

        return new AnnouncementSweepSettings(interval, gracePeriod, lookback, batchSize);
    }

    private static TimeSpan DurationAt(IConfiguration configuration, string key, TimeSpan fallback, TimeSpan least, TimeSpan most)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) || duration < least || duration > most)
        {
            throw new InvalidOperationException(
                $"The setting {key} is '{value}'; it accepts a duration from {Render(least)} to {Render(most)}, or nothing for {Render(fallback)}.");
        }

        return duration;
    }

    private static int CountAt(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultBatchSize;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < LeastBatchSize || count > MostBatchSize)
        {
            throw new InvalidOperationException(
                $"The setting {key} is '{value}'; it accepts a whole number from {LeastBatchSize} to {MostBatchSize}, or nothing for {DefaultBatchSize}.");
        }

        return count;
    }

    private static string Render(TimeSpan duration) => duration.ToString("c", CultureInfo.InvariantCulture);
}
