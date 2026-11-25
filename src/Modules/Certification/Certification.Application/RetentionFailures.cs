namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>
/// What the caller setting a pharmacy's retention period is told when it is refused (#391). A refusal by membership or
/// capability comes from <see cref="ExportFailures"/> through <see cref="CertificationAccess"/>, as for the export.
/// </summary>
public static class RetentionFailures
{
    public static ApiError InvalidPeriod() => new InvalidRetentionPeriod();
}
