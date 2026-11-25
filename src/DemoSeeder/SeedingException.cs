namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>A step of the seed failed; the message names the step and the user, never a credential or a page's content.</summary>
public sealed class SeedingException : Exception
{
    public SeedingException()
    {
    }

    public SeedingException(string message)
        : base(message)
    {
    }

    public SeedingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
