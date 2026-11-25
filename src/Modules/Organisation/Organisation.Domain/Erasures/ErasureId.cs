namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

/// <summary>Identifies one erasure request; the erasure log holds it, never the person's name or address.</summary>
public sealed record ErasureId : IValueObject<Guid>
{
    private ErasureId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ErasureId New() => throw new NotImplementedException();

    public static ErasureId From(Guid value) => throw new NotImplementedException();

    public override string ToString() => Value.ToString();
}
