using System.IO.Compression;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;

/// <summary>
/// A pharmacy's certificates for a period as one ZIP (#52, docs/adr/0031), written straight into a response body and
/// never stored: <see cref="CertificateExportIndex.FileName"/> first, then each stored PDF under <c>pdf/</c>, copied byte
/// for byte from the module's blob storage as it is written. A certificate whose PDF is still pending is in the index
/// alone. Everything is written asynchronously, as Kestrel requires, and each entry is dated by its certificate's issue
/// (the index by the period's start), so the same certificates give the same archive. A failure while writing leaves
/// the archive without its central directory; see #367 and docs/adr/0031.
/// </summary>
public sealed class CertificateExportArchive
{
    public const string ContentType = "application/zip";

    /// <summary>The most certificates one export holds.</summary>
    public const int MaxCertificates = 2000;

    private readonly ExportPeriod period;
    private readonly IBlobReads blobs;

    public CertificateExportArchive(ExportPeriod period, IReadOnlyList<Certificate> certificates, IBlobReads blobs)
    {
        Ensure.That(period).IsNotNull();
        Ensure.That(certificates).IsNotNull();
        Ensure.That(blobs).IsNotNull();

        this.period = period;
        Certificates = certificates;
        this.blobs = blobs;
    }

    /// <summary>The name the archive is downloaded under.</summary>
    public string FileName => $"teilnahmebestaetigungen_{period.From}_{period.To}.zip";

    /// <summary>The certificates in the archive, in the order of the index.</summary>
    public IReadOnlyList<Certificate> Certificates { get; }

    public async Task WriteAsync(Stream target, CancellationToken cancellationToken)
    {
        Ensure.That(target).IsNotNull();

        // Not `await using`: disposing writes the central directory, which makes the bytes a ZIP. It is disposed only once every
        // entry is complete, so a failure (a blob that cannot be opened or read) leaves no readable archive behind (#367); the
        // exception ends the response, which Kestrel and the edge abort since it has started.
        var archive = await ZipArchive.CreateAsync(target, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);

        var index = archive.CreateEntry(CertificateExportIndex.FileName, CompressionLevel.Fastest);
        index.LastWriteTime = period.Start.Value;
        await using (var content = await index.OpenAsync(cancellationToken))
        {
            await content.WriteAsync(CertificateExportIndex.Write(Certificates), cancellationToken);
        }

        foreach (var certificate in Certificates)
        {
            var pdf = CertificateExportIndex.StoredPdfOf(certificate);
            if (!pdf.HasValue)
            {
                continue;
            }

            var entry = archive.CreateEntry(CertificateExportIndex.EntryNameFor(certificate.Id), CompressionLevel.Fastest);
            entry.LastWriteTime = certificate.IssuedAt.Value;
            await using var blob = await blobs.OpenReadAsync(pdf.Value, cancellationToken);
            await using var content = await entry.OpenAsync(cancellationToken);
            await blob.CopyToAsync(content, cancellationToken);
        }

        await archive.DisposeAsync();
    }
}
