using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;

/// <summary>
/// Where a certificate's PDF lies in Certification's own container: derived from its id, so a retry or a replay finds
/// the blob an earlier attempt wrote (#50).
/// </summary>
public static class CertificateDocuments
{
    public const string ContentType = "application/pdf";

    public static BlobName NameFor(CertificateId id)
    {
        Ensure.That(id).IsNotNull();
        return BlobName.From($"certificates/{id.Value:D}.pdf");
    }
}
