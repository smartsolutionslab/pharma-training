using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A video's length in whole seconds; Learning uses it to judge whether progress is plausible.</summary>
public sealed record VideoDuration : IValueObject<int>
{
    private VideoDuration(int value) => Value = value;

    public int Value { get; }

    public static VideoDuration From(int seconds)
    {
        Ensure.That(seconds).InRange(1, 4 * 60 * 60);
        return new VideoDuration(seconds);
    }

    /// <summary>Red-test stub for #15 (task A2): refuses every value. B3 checks a caller's seconds as <see cref="From"/> does.</summary>
    public static bool TryFrom(int? seconds, [NotNullWhen(true)] out VideoDuration? result)
    {
        result = null;
        return false;
    }
}
