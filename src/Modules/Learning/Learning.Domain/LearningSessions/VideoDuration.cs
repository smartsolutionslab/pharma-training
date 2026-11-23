namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>
/// The length of a video chapter in whole seconds, from 1 to 4 hours, as Learning's copy of the published version
/// holds it. Learning judges against it whether reported progress is plausible (ADR 0024).
/// </summary>
public sealed record VideoDuration : IValueObject<int>
{
    public const int MaxSeconds = 4 * 60 * 60;

    private VideoDuration(int value) => Value = value;

    public int Value { get; }

    public static VideoDuration From(int value)
    {
        Ensure.That(value).InRange(1, MaxSeconds);
        return new VideoDuration(value);
    }
}
