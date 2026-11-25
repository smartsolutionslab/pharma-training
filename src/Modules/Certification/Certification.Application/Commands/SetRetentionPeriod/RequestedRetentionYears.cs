using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;

/// <summary>
/// The years a caller asked a pharmacy to keep certificates for, as the API received them (#422): absent, or any whole
/// number, in range or not. It refuses nothing, so the handler can check the caller's access first; only
/// <see cref="RetentionPeriod.TryFrom"/> turns it into a period (docs/adr/0039).
/// </summary>
public sealed record RequestedRetentionYears : IValueObject<int?>
{
    private RequestedRetentionYears(int? value) => Value = value;

    public int? Value { get; }

    public static RequestedRetentionYears From(int? value) => new(value);
}
