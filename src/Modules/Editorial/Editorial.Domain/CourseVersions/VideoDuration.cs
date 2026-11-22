using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A video's length in whole seconds, one to <see cref="MaxSeconds"/>; Learning uses it to judge whether progress is plausible.</summary>
public sealed record VideoDuration : IValueObject<int>
{
    public const int MaxSeconds = 4 * 60 * 60;

    private VideoDuration(int value) => Value = value;

    public int Value { get; }

    public static VideoDuration From(int seconds)
    {
        Ensure.That(seconds).InRange(1, MaxSeconds);
        return new VideoDuration(seconds);
    }

    /// <summary>Succeeds exactly when <see cref="From"/> would; no seconds are no duration.</summary>
    public static bool TryFrom(int? seconds, [NotNullWhen(true)] out VideoDuration? result)
    {
        result = seconds is >= 1 and <= MaxSeconds ? new VideoDuration(seconds.Value) : null;
        return result is not null;
    }
}
