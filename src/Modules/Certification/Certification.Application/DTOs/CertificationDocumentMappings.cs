using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

public static class CertificationDocumentMappings
{
    /// <summary>The read URL signed on a certificate's stored PDF.</summary>
    public static CertificationDocumentUrlDto ToDto(this BlobUrl url)
    {
        Ensure.That(url).IsNotNull();

        return new CertificationDocumentUrlDto(url.Url, url.ExpiresAt);
    }
}
