namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

/// <summary>The module that completed a step of an erasure, by its lower-case name (<c>organisation</c>, <c>compliance</c>, ...).</summary>
public sealed record ErasureModule : IValueObject<string>
{
    public const int MaxLength = 40;

    public static readonly ErasureModule Organisation = new("organisation");

    private ErasureModule(string value) => Value = value;

    public string Value { get; }

    public static ErasureModule From(string value) => throw new NotImplementedException();

    public override string ToString() => Value;
}
