using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

/// <summary>The module that completed a step of an erasure, by its lower-case name (<c>organisation</c>, <c>compliance</c>, ...).</summary>
public sealed partial record ErasureModule : IValueObject<string>
{
    public const int MaxLength = 40;

    public static readonly ErasureModule Organisation = new("organisation");

    /// <summary>The erased person's Keycloak account, which Organisation deletes.</summary>
    public static readonly ErasureModule Keycloak = new("keycloak");

    private ErasureModule(string value) => Value = value;

    public string Value { get; }

    public static ErasureModule From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Matches(Shape(), "A module's name is lower-case letters only.");
        return new ErasureModule(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z]+$")]
    private static partial Regex Shape();
}
