namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>What the caller of a certificate export is told when the module refuses (#52).</summary>
public static class ExportFailures
{
    public static ApiError PharmacyNotFound(Guid id) => new CertificationPharmacyNotFound(id);

    public static ApiError CapabilityMissing() => new CertificationCapabilityMissing();

    public static ApiError InvalidPeriod() => new InvalidExportPeriod();

    public static ApiError TooLarge() => new ExportTooLarge();
}
