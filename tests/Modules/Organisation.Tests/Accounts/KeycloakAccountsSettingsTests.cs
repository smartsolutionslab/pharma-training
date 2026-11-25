using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Accounts;

/// <summary>
/// #115 slice 6: the settings Organisation reaches Keycloak's admin API with, <c>Keycloak:Accounts</c>, which the AppHost
/// passes. A missing, blank or unusable value fails validation, naming its key and never a value, so a host without them
/// does not start.
/// </summary>
public class KeycloakAccountsSettingsTests
{
    private const string Secret = "erasure-client-secret-0123456789abcdef";

    [Fact]
    public void Complete_settings_are_bound_from_keycloak_accounts()
    {
        using var provider = Provider(Complete());

        var settings = provider.GetRequiredService<IOptions<KeycloakAccountsSettings>>().Value;

        settings.Address.ShouldBe("http://keycloak.internal:8080");
        settings.Realm.ShouldBe("pharma-training");
        settings.ClientId.ShouldBe("pharma-training-erasure");
        settings.ClientSecret.ShouldBe(Secret);
    }

    [Theory]
    [InlineData("Address", null)]
    [InlineData("Address", " ")]
    [InlineData("Address", "keycloak.internal:8080")]
    [InlineData("Address", "/relative")]
    [InlineData("Address", "ftp://keycloak.internal")]
    [InlineData("Realm", null)]
    [InlineData("Realm", "")]
    [InlineData("ClientId", null)]
    [InlineData("ClientId", " ")]
    [InlineData("ClientSecret", null)]
    [InlineData("ClientSecret", "  ")]
    public void A_missing_or_unusable_setting_fails_naming_its_key_and_no_value(string key, string? value)
    {
        var configuration = Complete();
        configuration[$"Keycloak:Accounts:{key}"] = value;
        using var provider = Provider(configuration);

        var failed = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<KeycloakAccountsSettings>>().Value);

        failed.Message.ShouldContain($"Keycloak:Accounts:{key}");
        failed.Message.ShouldNotContain(Secret);
    }

    private static Dictionary<string, string?> Complete() => new(StringComparer.Ordinal)
    {
        ["Keycloak:Accounts:Address"] = "http://keycloak.internal:8080",
        ["Keycloak:Accounts:Realm"] = "pharma-training",
        ["Keycloak:Accounts:ClientId"] = "pharma-training-erasure",
        ["Keycloak:Accounts:ClientSecret"] = Secret,
    };

    private static ServiceProvider Provider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddKeycloakAccounts(configuration);
        return services.BuildServiceProvider();
    }
}
