namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>A certificate's identity, minted by Certification when it issues one: a time-ordered Guid v7. An empty Guid is never one.</summary>
public sealed record CertificateId : IValueObject<Guid>
{
    private CertificateId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CertificateId New() => new(Guid.CreateVersion7());

    public static CertificateId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new CertificateId(value);
    }

    public override string ToString() => Value.ToString();
}
