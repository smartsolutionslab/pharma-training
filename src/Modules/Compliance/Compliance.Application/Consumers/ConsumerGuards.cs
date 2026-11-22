namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>The checks the consumers share on what another module sent.</summary>
internal static class ConsumerGuards
{
    /// <summary>
    /// Text that is there, not blank, no longer than the publisher allows, and holding no NUL character, which
    /// PostgreSQL refuses.
    /// </summary>
    public static void IsText(string value, string name, int maxLength) =>
        Ensure.That(value, name).IsNotNullOrWhiteSpace().HasMaxLength(maxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "must not contain a NUL character.");
}
