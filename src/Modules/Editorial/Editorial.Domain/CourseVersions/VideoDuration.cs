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
}
