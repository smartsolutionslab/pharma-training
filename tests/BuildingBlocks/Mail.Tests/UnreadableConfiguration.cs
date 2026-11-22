using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>Configuration that fails any read, so a test sees that registering and resolving read nothing.</summary>
internal sealed class UnreadableConfiguration : IConfiguration
{
    public string? this[string key]
    {
        get => throw Read(key);
        set => throw Read(key);
    }

    public IEnumerable<IConfigurationSection> GetChildren() => throw Read("children");

    public IChangeToken GetReloadToken() => throw Read("reload token");

    public IConfigurationSection GetSection(string key) => throw Read(key);

    private static InvalidOperationException Read(string key) => new($"The configuration was read ({key}) while registering.");
}
