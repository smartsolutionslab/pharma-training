using System.IO.Compression;
using System.Text;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>
/// The export's ZIP (#52), written straight into the response body: <c>index.csv</c> first, then each stored PDF under
/// <c>pdf/</c>, byte for byte as blob storage holds it; a certificate whose PDF is still pending is in the index alone.
/// The body is written asynchronously only, as Kestrel requires, and the same data gives the same archive.
/// </summary>
public class CertificateExportArchiveTests
{
    private static readonly PersonId Holder = PersonId.From(Guid.CreateVersion7());

    private readonly InMemoryBlobReads blobs = new();

    [Fact]
    public void It_is_served_as_a_zip_named_after_its_period()
    {
        var archive = new CertificateExportArchive(Period(), [], blobs);

        CertificateExportArchive.ContentType.ShouldBe("application/zip");
        archive.FileName.ShouldBe("teilnahmebestaetigungen_2025-05-01_2025-06-30.zip");
    }

    [Fact]
    public void It_holds_at_most_two_thousand_certificates()
    {
        CertificateExportArchive.MaxCertificates.ShouldBe(2000);
    }

    [Fact]
    public void It_keeps_the_certificates_it_was_given()
    {
        var certificate = Stored(new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));

        new CertificateExportArchive(Period(), [certificate], blobs).Certificates.ShouldBe([certificate]);
    }

    [Fact]
    public async Task The_index_comes_first_then_one_pdf_per_stored_certificate_with_the_blobs_bytes()
    {
        var first = Stored(new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var pending = ExportCertificates.Pending(Holder, new DateTimeOffset(2025, 5, 20, 9, 0, 0, TimeSpan.Zero));
        var second = Stored(new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero));
        List<Certificate> certificates = [first, pending, second];

        var entries = await EntriesOfAsync(new CertificateExportArchive(Period(), certificates, blobs));

        entries.Select(entry => entry.Name).ShouldBe([
            "index.csv",
            $"pdf/{first.Id.Value:D}.pdf",
            $"pdf/{second.Id.Value:D}.pdf",
        ]);
        entries[0].Content.ShouldBe(CertificateExportIndex.Write(certificates));
        entries[1].Content.ShouldBe(blobs.Blobs[BlobName.From(ExportCertificates.LocationOf(first))]);
        entries[2].Content.ShouldBe(blobs.Blobs[BlobName.From(ExportCertificates.LocationOf(second))]);
    }

    [Fact]
    public async Task A_pending_pdf_is_neither_read_nor_written()
    {
        var pending = ExportCertificates.Pending(Holder, new DateTimeOffset(2025, 5, 20, 9, 0, 0, TimeSpan.Zero));

        var entries = await EntriesOfAsync(new CertificateExportArchive(Period(), [pending], blobs));

        entries.ShouldHaveSingleItem().Name.ShouldBe("index.csv");
        blobs.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_stored_pdf_is_read_from_its_document_location()
    {
        var first = Stored(new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var second = Stored(new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero));

        await EntriesOfAsync(new CertificateExportArchive(Period(), [first, second], blobs));

        blobs.Reads.ShouldBe([BlobName.From(ExportCertificates.LocationOf(first)), BlobName.From(ExportCertificates.LocationOf(second))]);
    }

    [Fact]
    public async Task Without_certificates_it_holds_the_index_with_its_header_alone()
    {
        var entries = await EntriesOfAsync(new CertificateExportArchive(Period(), [], blobs));

        entries.ShouldHaveSingleItem().Content.ShouldBe(CertificateExportIndex.Write([]));
    }

    [Fact]
    public async Task The_same_certificates_give_the_same_archive()
    {
        var certificate = Stored(new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var archive = new CertificateExportArchive(Period(), [certificate], blobs);

        var first = await WrittenAsync(archive);
        var second = await WrittenAsync(archive);

        second.ShouldBe(first);
    }

    [Fact]
    public async Task A_stored_pdf_missing_from_blob_storage_fails_the_export()
    {
        var certificate = ExportCertificates.Stored(Holder, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var archive = new CertificateExportArchive(Period(), [certificate], blobs);

        await Should.ThrowAsync<InvalidOperationException>(() => WrittenAsync(archive));
    }

    [Fact]
    public async Task It_writes_into_a_stream_that_refuses_synchronous_writes_and_cannot_seek()
    {
        var certificate = Stored(new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var archive = new CertificateExportArchive(Period(), [certificate], blobs);
        await using var body = new AsyncOnlyStream();

        await archive.WriteAsync(body, TestContext.Current.CancellationToken);

        var entries = Read(body.Written);
        entries.Select(entry => entry.Name).ShouldBe(["index.csv", $"pdf/{certificate.Id.Value:D}.pdf"]);
    }

    /// <summary>
    /// #367: once the index has gone out the response has started, so a blob that cannot be opened must end it without the
    /// central directory; the bytes sent are then no readable ZIP, never a valid archive missing a PDF.
    /// </summary>
    [Fact]
    public async Task A_blob_that_fails_to_open_leaves_no_readable_zip()
    {
        var certificate = ExportCertificates.Stored(Holder, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var archive = new CertificateExportArchive(Period(), [certificate], FailingBlobReads.AtOpen());
        await using var body = new AsyncOnlyStream();

        await Should.ThrowAsync<IOException>(() => archive.WriteAsync(body, TestContext.Current.CancellationToken));

        body.Written.ShouldNotBeEmpty("the index went out first, so the response had started");
        Should.Throw<InvalidDataException>(() => Read(body.Written));
    }

    /// <summary>#367: a blob whose connection is lost while it is copied ends the response without the central directory too.</summary>
    [Fact]
    public async Task A_blob_that_fails_while_it_is_copied_leaves_no_readable_zip()
    {
        var certificate = ExportCertificates.Stored(Holder, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var archive = new CertificateExportArchive(Period(), [certificate], FailingBlobReads.WhileCopying());
        await using var body = new AsyncOnlyStream();

        await Should.ThrowAsync<IOException>(() => archive.WriteAsync(body, TestContext.Current.CancellationToken));

        body.Written.ShouldNotBeEmpty("the index went out first, so the response had started");
        Should.Throw<InvalidDataException>(() => Read(body.Written));
    }

    /// <summary>
    /// #367: the index and the archive decide "has a PDF" by one condition, so a row marked stored without a location
    /// (which the domain never writes, but a database can hold) is pending in both.
    /// </summary>
    [Fact]
    public async Task The_index_names_exactly_the_pdfs_the_archive_holds()
    {
        var pending = ExportCertificates.Pending(Holder, new DateTimeOffset(2025, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var stored = Stored(new DateTimeOffset(2025, 5, 20, 9, 0, 0, TimeSpan.Zero));
        var withoutLocation = ExportCertificates.StoredWithoutLocation(Holder, new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero));

        var entries = await EntriesOfAsync(new CertificateExportArchive(Period(), [pending, stored, withoutLocation], blobs));

        var index = Encoding.UTF8.GetString(entries[0].Content, 3, entries[0].Content.Length - 3);
        var named = index[..^2].Split("\r\n").Skip(1)
            .Select(row => row.Split(';')[^1])
            .Where(pdf => pdf != CertificateExportIndex.PendingMarker)
            .ToList();
        named.ShouldBe(entries.Skip(1).Select(entry => entry.Name).ToList());
    }

    private static ExportPeriod Period() => ExportPeriod.Of(new DateOnly(2025, 5, 1), new DateOnly(2025, 6, 30)).Value;

    private static async Task<byte[]> WrittenAsync(CertificateExportArchive archive)
    {
        await using var body = new AsyncOnlyStream();
        await archive.WriteAsync(body, TestContext.Current.CancellationToken);
        return body.Written;
    }

    private static async Task<List<(string Name, byte[] Content)>> EntriesOfAsync(CertificateExportArchive archive) =>
        Read(await WrittenAsync(archive));

    private static List<(string Name, byte[] Content)> Read(byte[] zip)
    {
        using var zipStream = new MemoryStream(zip, writable: false);
        using var read = new ZipArchive(zipStream, ZipArchiveMode.Read);
        List<(string Name, byte[] Content)> entries = [];
        foreach (var entry in read.Entries)
        {
            using var content = entry.Open();
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            entries.Add((entry.FullName, copy.ToArray()));
        }

        return entries;
    }

    private Certificate Stored(DateTimeOffset issuedAt)
    {
        var certificate = ExportCertificates.Stored(Holder, issuedAt);
        blobs.Blobs[BlobName.From(ExportCertificates.LocationOf(certificate))] = [.. "%PDF-1.7 "u8, .. certificate.Id.Value.ToByteArray()];
        return certificate;
    }
}
