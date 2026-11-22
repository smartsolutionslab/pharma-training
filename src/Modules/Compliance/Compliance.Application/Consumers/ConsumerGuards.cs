namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>The checks the consumers share on what another module sent.</summary>
internal static class ConsumerGuards
{
    /// <summary>Text that is there, not blank, and holds no NUL character, which PostgreSQL refuses.</summary>
    public static void IsText(string value, string name) =>
        Ensure.That(value, name).IsNotNullOrWhiteSpace().Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "must not contain a NUL character.");
}
