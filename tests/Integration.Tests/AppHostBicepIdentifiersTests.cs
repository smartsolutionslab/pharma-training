namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Every symbol the publish writes is one Bicep accepts: an identifier longer than 255 characters fails the deployment
/// with BCP024 before anything is created (#183, where Aspire named a Key Vault module's parameter after a whole
/// definitions document). Aspire names parameters after the expressions it is handed, so any expression can do it again.
/// </summary>
public class AppHostBicepIdentifiersTests
{
    /// <summary>Bicep's limit on an identifier's length (BCP024).</summary>
    private const int MaximumIdentifierLength = 255;

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task No_published_bicep_declares_an_identifier_longer_than_bicep_allows(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var identifiers = bicep.Files
            .Where(file => file.Key.EndsWith(".bicep", StringComparison.Ordinal))
            .SelectMany(file => PublishedBicep.DeclaredIdentifiers(file.Value).Select(identifier => (File: file.Key, identifier.Name, identifier.Line)))
            .ToList();
        var tooLong = identifiers
            .Where(identifier => identifier.Name.Length > MaximumIdentifierLength)
            .Select(identifier => $"{identifier.File}({identifier.Line}): {identifier.Name.Length} characters, {identifier.Name[..60]}...")
            .ToList();

        identifiers.ShouldContain(identifier => identifier.File == "main.bicep", "No declaration was found in main.bicep; the test reads no identifier at all.");
        tooLong.ShouldBeEmpty($"Bicep refuses an identifier longer than {MaximumIdentifierLength} characters (BCP024).");
    }
}
