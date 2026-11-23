namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>Stub for #43's red run: the length of a video chapter in whole seconds, not yet guarded.</summary>
public sealed record VideoDuration : IValueObject<int>
{
    public const int MaxSeconds = 4 * 60 * 60;

    private VideoDuration(int value) => Value = value;

    public int Value { get; }

    public static VideoDuration From(int value) => new(value);
}
