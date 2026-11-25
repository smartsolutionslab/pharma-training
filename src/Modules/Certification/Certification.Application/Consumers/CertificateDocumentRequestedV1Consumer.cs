using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>
/// Renders a certificate's PDF once its issue is committed, from the host's durable local queue (#50, docs/adr/0030):
/// from the certificate's own snapshot, with the verification link into the web app, written create-only to
/// <c>certificates/{id}.pdf</c> in Certification's container and attached in one commit. A certificate whose document is
/// stored is never rendered again, and a blob already under the name (an earlier attempt that wrote and then failed to
/// commit, or a parallel delivery) is kept and attached as it is: the first bytes stored win. It deduplicates on the
/// certificate's state and the derived blob name, not on <c>Metadata.EventIdentifier</c>. Anything that fails throws, so
/// Wolverine retries the message and dead-letters it at last, and a replay finishes what is left.
/// <para>
/// An erasure (#115) deletes the certificates no pharmacy keeps, PDF first, so a request may find its certificate gone:
/// it is logged and dropped. The certificate is loaded first and its holder then held, as the erasure holds them, so a
/// render either commits before the erasure reads the holder's certificates (and its PDF is deleted there) or waits and
/// finds the holder tombstoned, and is dropped before anything is written.
/// </para>
/// </summary>
public sealed partial class CertificateDocumentRequestedV1Consumer(
    ICertificateStore certificates,
    IErasedPersonStore erased,
    ICertificationUnitOfWork unitOfWork,
    ICertificateRenderer renderer,
    IBlobWrites blobs,
    IFrontendLinks links,
    ILogger<CertificateDocumentRequestedV1Consumer> logger)
{
    public async Task HandleAsync(CertificateDocumentRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.CertificateId, nameof(message.CertificateId)).IsNotEmpty();

        var id = CertificateId.From(message.CertificateId);
        var found = await certificates.LoadAsync(id, cancellationToken);
        if (!found.HasValue)
        {
            // Saved in the same commit as this request, so it is gone only because an erasure removed it (#115).
            CertificateGone(logger, message.CertificateId);
            return;
        }

        var certificate = found.Value;
        await erased.HoldAsync(certificate.Holder, cancellationToken);
        if (await erased.IsErasedAsync(certificate.Holder, cancellationToken))
        {
            HolderErased(logger, message.CertificateId);
            return;
        }

        if (certificate.Document == DocumentState.Stored)
        {
            AlreadyStored(logger, message.CertificateId);
            return;
        }

        // Composed before rendering, so a missing web app address fails before anything is written.
        var link = links.LinkTo($"/verify/{certificate.VerificationCode.Value}");
        var pdf = renderer.Render(certificate.ToContent(link));
        var name = CertificateDocuments.NameFor(id);
        if (await blobs.CreateAsync(name, BinaryData.FromBytes(pdf), CertificateDocuments.ContentType, cancellationToken) == BlobWriteOutcome.AlreadyThere)
        {
            KeptEarlierBlob(logger, message.CertificateId);
        }

        certificate.AttachDocument(DocumentReference.From(name.Value));
        await unitOfWork.CommitAsync(cancellationToken);
        Stored(logger, message.CertificateId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Certificate {Certificate} is no longer stored, an erasure removed it; its document request is dropped.")]
    private static partial void CertificateGone(ILogger logger, Guid certificate);

    [LoggerMessage(Level = LogLevel.Information, Message = "The holder of certificate {Certificate} was erased; its document request is dropped.")]
    private static partial void HolderErased(ILogger logger, Guid certificate);

    [LoggerMessage(Level = LogLevel.Information, Message = "The document of certificate {Certificate} is stored already; it is not rendered again.")]
    private static partial void AlreadyStored(ILogger logger, Guid certificate);

    [LoggerMessage(Level = LogLevel.Information, Message = "A document of certificate {Certificate} lay in the container already; it is kept and attached.")]
    private static partial void KeptEarlierBlob(ILogger logger, Guid certificate);

    [LoggerMessage(Level = LogLevel.Information, Message = "The document of certificate {Certificate} is stored.")]
    private static partial void Stored(ILogger logger, Guid certificate);
}
