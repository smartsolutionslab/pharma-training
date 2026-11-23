namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>A certificate's identity, as Certification issued it. An empty Guid is never one.</summary>
public sealed record CertificateId : IValueObject<Guid>
{
    private CertificateId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CertificateId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CertificateId(value);
    }

    public override string ToString() => Value.ToString();
}
