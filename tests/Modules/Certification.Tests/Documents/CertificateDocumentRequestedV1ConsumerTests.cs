using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Documents;

/// <summary>
/// The certificate's PDF is rendered once, from the certificate's own snapshot, stored create-only at
/// <c>certificates/{id}.pdf</c> in Certification's container and attached (#50). A stored certificate is never rendered
/// again, and the first bytes stored win: a retry or a replay finds the blob already there and attaches it as it is.
/// Anything that fails throws, so Wolverine retries the message and dead-letters it at last.
/// </summary>
public class CertificateDocumentRequestedV1ConsumerTests
{
    private readonly InMemoryCertificateStore certificates = new();
    private readonly FakeCertificateRenderer renderer = new();
    private readonly InMemoryBlobWrites blobs = new();
    private readonly InMemoryErasedPersonStore erased = new();
    private readonly PersonId holder = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_pending_certificate_is_rendered_once_stored_at_its_name_as_a_pdf_and_attached_in_one_commit()
    {
        var certificate = Stored(TestCertificates.Issued(holder));
        var name = BlobName.From($"certificates/{certificate.Id.Value:D}.pdf");

        await HandleAsync(Requested(certificate));

        renderer.Rendering.ShouldHaveSingleItem();
        var write = blobs.Writes.ShouldHaveSingleItem();
        write.Name.ShouldBe(name);
        write.ContentType.ShouldBe("application/pdf");
        write.Content.ShouldBe(FakeCertificateRenderer.Rendered);
        certificate.Document.ShouldBe(DocumentState.Stored);
        certificate.DocumentLocation.ShouldBe(DocumentReference.From(name.Value));
        certificates.Commits.ShouldBe(1);
    }

    [Fact]
    public void A_certificates_document_is_named_after_its_id()
    {
        var id = CertificateId.New();

        CertificateDocuments.NameFor(id).ShouldBe(BlobName.From($"certificates/{id.Value:D}.pdf"));
    }

    /// <summary>The reference test for "never re-rendered", beside the aggregate's own: AttachDocument keeps the first reference.</summary>
    [Fact]
    public async Task A_stored_certificate_is_never_rendered_or_written_again()
    {
        var certificate = Stored(TestCertificates.Issued(holder));
        var first = DocumentReference.From($"certificates/{certificate.Id.Value:D}.pdf");
        certificate.AttachDocument(first);

        await HandleAsync(Requested(certificate));

        renderer.Rendering.ShouldBeEmpty();
        blobs.Writes.ShouldBeEmpty();
        certificate.DocumentLocation.ShouldBe(first);
        certificates.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task The_same_request_delivered_twice_renders_and_writes_once()
    {
        var certificate = Stored(TestCertificates.Issued(holder));
        var requested = Requested(certificate);

        await HandleAsync(requested);
        await HandleAsync(requested);

        renderer.Rendering.ShouldHaveSingleItem();
        blobs.Writes.ShouldHaveSingleItem();
        certificates.Commits.ShouldBe(1);
    }

    /// <summary>An earlier attempt wrote the blob and then failed to commit: the blob is kept as it is and attached.</summary>
    [Fact]
    public async Task A_blob_already_there_is_attached_and_its_first_bytes_are_kept()
    {
        var certificate = Stored(TestCertificates.Issued(holder));
        var name = CertificateDocuments.NameFor(certificate.Id);
        var earlier = "%PDF-1.7 earlier attempt"u8.ToArray();
        blobs.Blobs[name] = (earlier, "application/pdf");

        await HandleAsync(Requested(certificate));

        blobs.Blobs[name].Content.ShouldBe(earlier, "the first bytes stored win and are never overwritten");
        certificate.Document.ShouldBe(DocumentState.Stored);
        certificate.DocumentLocation.ShouldBe(DocumentReference.From(name.Value));
        certificates.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_renderer_that_fails_throws_to_be_retried_and_nothing_is_written_or_attached()
    {
        var certificate = Stored(TestCertificates.Issued(holder));
        renderer.Failure = new InvalidOperationException("The font is missing.");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(Requested(certificate)));

        thrown.Message.ShouldBe("The font is missing.");
        blobs.Writes.ShouldBeEmpty();
        certificate.Document.ShouldBe(DocumentState.Pending);
        certificate.DocumentLocation.ShouldBeNull();
        certificates.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Without_the_web_apps_address_it_throws_naming_the_setting_and_nothing_is_written_or_attached()
    {
        var certificate = Stored(TestCertificates.Issued(holder));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
            HandleAsync(Requested(certificate), new FrontendLinks(new ConfigurationBuilder().Build())));

        thrown.Message.ShouldContain("Frontend:BaseUrl");
        renderer.Rendering.ShouldBeEmpty();
        blobs.Writes.ShouldBeEmpty();
        certificate.Document.ShouldBe(DocumentState.Pending);
        certificates.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task The_pdf_shows_the_holders_name_the_course_its_version_the_german_day_the_score_the_last_valid_day_and_the_verification_link()
    {
        var certificate = Stored(TestCertificates.Issued(holder, "Anna Müller-Lüdenscheidt", "Kühlkette in der Apotheke"));

        await HandleAsync(Requested(certificate));

        var content = renderer.Rendering.ShouldHaveSingleItem();
        content.HolderName.ShouldBe("Anna Müller-Lüdenscheidt");
        content.CourseTitle.ShouldBe("Kühlkette in der Apotheke");
        content.CourseVersionNumber.ShouldBe(3);
        content.Statement.ShouldBe("hat die Schulung „Kühlkette in der Apotheke“ (Version 3) erfolgreich abgeschlossen.");
        content.IssuedOn.ShouldBe("02.03.2027", "23:30 UTC on 1 March is already 2 March in Germany");
        content.Score.ShouldBe("85 %");
        content.ValidUntil.ShouldBe("02.03.2028");
        content.VerificationLink.ShouldBe(new Uri($"https://app.example/verify/{TestCertificates.Code}"));
        content.IssuedAt.ShouldBe(TestCertificates.PassedAt);
    }

    [Fact]
    public async Task An_empty_certificate_id_is_refused_by_its_name_and_nothing_is_rendered()
    {
        var refused = await Should.ThrowAsync<GuardException>(() => HandleAsync(new CertificateDocumentRequestedV1(Guid.Empty) { Metadata = NewMetadata() }));

        refused.ParamName.ShouldBe(nameof(CertificateDocumentRequestedV1.CertificateId));
        NothingDone();
    }

    /// <summary>
    /// #115: an erasure deletes a certificate no pharmacy keeps, so a request for its document can find it gone. It is
    /// dropped and logged by the certificate's id, not thrown, so it neither retries nor dead-letters, and no PDF is
    /// written back.
    /// </summary>
    [Fact]
    public async Task A_certificate_that_no_longer_exists_is_dropped_and_logged_and_nothing_is_rendered()
    {
        var gone = Guid.CreateVersion7();
        var logger = new FakeLogger<CertificateDocumentRequestedV1Consumer>();

        await new CertificateDocumentRequestedV1Consumer(certificates, erased, certificates, renderer, blobs, Links(), logger)
            .HandleAsync(new CertificateDocumentRequestedV1(gone) { Metadata = NewMetadata() }, TestContext.Current.CancellationToken);

        NothingDone();
        logger.Collector.GetSnapshot().ShouldHaveSingleItem().Message.ShouldContain(gone.ToString());
    }

    /// <summary>
    /// #115: a render racing an erasure must not write the holder's PDF back after the erasure deleted it. The consumer
    /// holds the certificate's holder, as the erasure does, and drops the request once the erasure removed the certificate.
    /// </summary>
    [Fact]
    public async Task A_request_whose_certificate_an_erasure_removed_while_it_waited_for_the_hold_is_dropped_and_nothing_is_rendered()
    {
        var certificate = Stored(TestCertificates.Issued(holder));
        erased.WhileHolding = () =>
        {
            certificates.Stored.Remove(certificate);
            erased.Tombstone(holder, TestCertificates.PassedAt);
        };

        await HandleAsync(Requested(certificate));

        NothingDone();
        certificate.Document.ShouldBe(DocumentState.Pending);
    }

    /// <summary>
    /// #438 review: a certificate still stored after its holder was erased is a retained one (docs/adr/0036, decision 3),
    /// kept with its name and PDF, so a render that was retried or replayed after the erasure still stores its PDF.
    /// </summary>
    [Fact]
    public async Task A_retained_certificate_of_an_erased_holder_is_still_rendered_and_stored()
    {
        var certificate = Stored(TestCertificates.Issued(holder));
        erased.Tombstone(holder, TestCertificates.PassedAt);

        await HandleAsync(Requested(certificate));

        renderer.Rendering.ShouldHaveSingleItem();
        blobs.Writes.ShouldHaveSingleItem();
        certificate.Document.ShouldBe(DocumentState.Stored);
        certificates.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_holder_is_held_and_checked_before_a_certificate_is_rendered()
    {
        var certificate = Stored(TestCertificates.Issued(holder));

        await HandleAsync(Requested(certificate));

        erased.Journal.ShouldBe(["hold", "is-erased"]);
        renderer.Rendering.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_is_rendered()
    {
        await Should.ThrowAsync<GuardException>(() => HandleAsync(null!));

        NothingDone();
    }

    private static CertificateDocumentRequestedV1 Requested(Certificate certificate) =>
        new(certificate.Id.Value) { Metadata = NewMetadata() };

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TestCertificates.PassedAt);

    private static FrontendLinks Links() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Frontend:BaseUrl", "https://app.example")])
        .Build());

    private Certificate Stored(Certificate certificate)
    {
        certificates.Stored.Add(certificate);
        return certificate;
    }

    private Task HandleAsync(CertificateDocumentRequestedV1 message, IFrontendLinks? links = null) =>
        new CertificateDocumentRequestedV1Consumer(certificates, erased, certificates, renderer, blobs, links ?? Links(), NullLogger<CertificateDocumentRequestedV1Consumer>.Instance)
            .HandleAsync(message, TestContext.Current.CancellationToken);

    private void NothingDone()
    {
        renderer.Rendering.ShouldBeEmpty();
        blobs.Writes.ShouldBeEmpty();
        certificates.Commits.ShouldBe(0);
    }
}
