namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>One look at something the seeder waits for: its value once it is there, or why it is not yet.</summary>
internal sealed record Polled<T>(T? Value, string NotYet)
    where T : class
{
    public static Polled<T> Done(T value) => new(value, string.Empty);

    public static Polled<T> Wait(string reason) => new(null, reason);
}
