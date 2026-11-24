namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>What a caller of a certificate is told when the module refuses (#50).</summary>
public static class CertificateFailures
{
    public static ApiError IdentityIncomplete() => new CertificationIdentityIncomplete();

    public static ApiError NotFound(Guid id) => new CertificateNotFound(id);

    public static ApiError DocumentNotReady(Guid id) => new CertificateDocumentNotReady(id);
}
