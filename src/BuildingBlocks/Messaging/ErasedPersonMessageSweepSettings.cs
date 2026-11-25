using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// How a module's sweep of erased persons' messages (#439, docs/adr/0036) runs, read once at startup from the module's
/// own section, <c>&lt;Module&gt;:ErasedPersonMessageSweep</c>. A blank or absent interval takes the default; one that does
/// not parse or lies outside one second to one hour stops the host, naming the key, the value and what it accepts. The
/// interval is read in exactly <c>hh:mm:ss</c> or <c>d.hh:mm:ss</c> (<see cref="SettingDuration"/>).
/// </summary>
public sealed record ErasedPersonMessageSweepSettings
{
    public const string Section = "ErasedPersonMessageSweep";

    public const string IntervalKey = "Interval";

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan LeastInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MostInterval = TimeSpan.FromHours(1);

    private ErasedPersonMessageSweepSettings(TimeSpan interval)
    {
        Interval = interval;
    }

    /// <summary>The time between two passes, and before the first.</summary>
    public TimeSpan Interval { get; }

    /// <summary>The module's settings, from <c>&lt;module&gt;:ErasedPersonMessageSweep</c>.</summary>
    public static ErasedPersonMessageSweepSettings From(IConfiguration configuration, string module)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(module).IsNotNullOrWhiteSpace();

        var key = $"{module}:{Section}:{IntervalKey}";
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ErasedPersonMessageSweepSettings(DefaultInterval);
        }

        if (!SettingDuration.TryParse(value, out var interval) || interval < LeastInterval || interval > MostInterval)
        {
            throw new InvalidOperationException(
                $"The setting {key} is '{value}'; it accepts a duration from {Render(LeastInterval)} to {Render(MostInterval)}, or nothing for {Render(DefaultInterval)}.");
        }

        return new ErasedPersonMessageSweepSettings(interval);
    }

    private static string Render(TimeSpan duration) => duration.ToString("c", CultureInfo.InvariantCulture);
}
