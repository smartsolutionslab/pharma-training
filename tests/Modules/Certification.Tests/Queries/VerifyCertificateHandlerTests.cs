using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.VerifyCertificate;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Queries;

/// <summary>
/// Anyone with a certificate's QR code sees what it certifies (#51, Z2): the holder's name and the course title as
/// issued, the German day it was issued, its last valid day, and whether it is valid on today's German day. Every code
/// that finds no certificate, malformed or not, a certificate's id included, gets the one same refusal, which never
/// echoes the code; a malformed one is refused without a lookup.
/// </summary>
public class VerifyCertificateHandlerTests
{
    private readonly InMemoryCertificateStore store = new();

    [Fact]
    public async Task A_stored_code_shows_the_holder_the_course_both_days_and_valid_through_the_last_german_day()
    {
        var certificate = Stored();
        var time = new FakeTimeProvider(new DateTimeOffset(2028, 3, 2, 22, 59, 0, TimeSpan.Zero)); // 23:59 on 2 March in Germany

        var result = await new VerifyCertificateHandler(store, time).HandleAsync(new VerifyCertificate(certificate.VerificationCode.Value), TestContext.Current.CancellationToken);

        var verified = result.Value;
        verified.HolderName.ShouldBe("Anna Berger");
        verified.CourseTitle.ShouldBe("Hygiene 2027");
        verified.IssuedOn.ShouldBe(new DateOnly(2027, 3, 2));
        verified.ValidUntil.ShouldBe(new DateOnly(2028, 3, 2));
        verified.Validity.ShouldBe(CertificateValidity.Valid);
    }

    [Fact]
    public async Task A_stored_code_is_expired_from_the_next_german_day()
    {
        var certificate = Stored();
        var time = new FakeTimeProvider(new DateTimeOffset(2028, 3, 2, 23, 0, 0, TimeSpan.Zero)); // 00:00 on 3 March in Germany

        var result = await new VerifyCertificateHandler(store, time).HandleAsync(new VerifyCertificate(certificate.VerificationCode.Value), TestContext.Current.CancellationToken);

        result.Value.Validity.ShouldBe(CertificateValidity.Expired);
        result.Value.ValidUntil.ShouldBe(new DateOnly(2028, 3, 2));
    }

    [Fact]
    public async Task The_answer_follows_the_clock_the_handler_is_given()
    {
        var certificate = Stored();
        var time = new FakeTimeProvider(new DateTimeOffset(2028, 3, 2, 22, 59, 0, TimeSpan.Zero));
        var handler = new VerifyCertificateHandler(store, time);
        var query = new VerifyCertificate(certificate.VerificationCode.Value);
        (await handler.HandleAsync(query, TestContext.Current.CancellationToken)).Value.Validity.ShouldBe(CertificateValidity.Valid);
        time.Advance(TimeSpan.FromMinutes(1));

        var later = await handler.HandleAsync(query, TestContext.Current.CancellationToken);

        later.Value.Validity.ShouldBe(CertificateValidity.Expired);
    }

    [Theory]
    [InlineData("k7Qp2xV9mL4rT8wZ1nB6cY3hAbCdEfGh", 1)] // well formed, 32 characters, but no certificate's
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("k", 23)]
    [InlineData("k", 65)]
    [InlineData("abc", 1)]
    public async Task A_code_that_finds_no_certificate_is_not_verifiable_and_not_echoed(string value, int repeat)
    {
        Stored();
        var code = string.Concat(Enumerable.Repeat(value, repeat));

        var result = await Handler().HandleAsync(new VerifyCertificate(code), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(VerificationFailures.NotVerifiable());
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        result.Error.Code.ShouldBe("certification.certificate-not-verifiable");
        if (!string.IsNullOrWhiteSpace(code))
        {
            result.Error.Message.ShouldNotContain(code);
        }
    }

    [Fact]
    public async Task A_certificates_own_id_is_no_code()
    {
        var certificate = Stored();

        var result = await Handler().HandleAsync(new VerifyCertificate(certificate.Id.Value.ToString()), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(VerificationFailures.NotVerifiable());
        result.Error.Message.ShouldNotContain(certificate.Id.Value.ToString());
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("k", 23)]
    [InlineData("k", 65)]
    public async Task A_malformed_code_is_refused_without_a_lookup(string value, int repeat)
    {
        var result = await Handler().HandleAsync(new VerifyCertificate(string.Concat(Enumerable.Repeat(value, repeat))), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(VerificationFailures.NotVerifiable());
        store.VerificationLookups.ShouldBe(0);
    }

    [Fact]
    public async Task A_well_formed_code_is_looked_up_once()
    {
        await Handler().HandleAsync(new VerifyCertificate("k7Qp2xV9mL4rT8wZ1nB6cY3hAbCdEfGh"), TestContext.Current.CancellationToken);

        store.VerificationLookups.ShouldBe(1);
    }

    private VerifyCertificateHandler Handler() =>
        new(store, new FakeTimeProvider(new DateTimeOffset(2027, 6, 1, 9, 0, 0, TimeSpan.Zero)));

    private Certificate Stored()
    {
        var certificate = IssuedCertificates.Yearly();
        store.Stored.Add(certificate);
        return certificate;
    }
}
