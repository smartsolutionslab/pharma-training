namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>The number Editorial gave a course version when it published it, from 1, as the certificate prints it.</summary>
public sealed record CourseVersionNumber : IValueObject<int>
{
    public const int Minimum = 1;

    private CourseVersionNumber(int value) => Value = value;

    public int Value { get; }

    public static CourseVersionNumber From(int value)
    {
        Ensure.That(value).AtLeast(Minimum);
        return new CourseVersionNumber(value);
    }
}
