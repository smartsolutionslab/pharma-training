using System.Diagnostics;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>
/// What a verifier is shown of a certificate on a given day (#51): the holder's name and course title as issued, the
/// German day of issue, the last valid day and the validity on that day, and nothing else: no id, person, course,
/// score, attempt, document, pharmacy or code. The validity travels by name.
/// </summary>
public class CertificateMappingsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_certificate_on_its_last_valid_day_is_shown_valid_with_both_days()
    {
        var certificate = IssuedCertificates.Yearly();

        var verified = certificate.ToVerificationDto(CalendarDate.From(new DateOnly(2028, 3, 2)));

        verified.HolderName.ShouldBe("Anna Berger");
        verified.CourseTitle.ShouldBe("Hygiene 2027");
        verified.IssuedOn.ShouldBe(new DateOnly(2027, 3, 2));
        verified.ValidUntil.ShouldBe(new DateOnly(2028, 3, 2));
        verified.Validity.ShouldBe(CertificateValidity.Valid);
    }

    [Fact]
    public void A_certificate_the_day_after_its_last_valid_day_is_shown_expired()
    {
        var verified = IssuedCertificates.Yearly().ToVerificationDto(CalendarDate.From(new DateOnly(2028, 3, 3)));

        verified.Validity.ShouldBe(CertificateValidity.Expired);
    }

    [Fact]
    public void The_answer_has_exactly_the_five_fields_a_verifier_sees()
    {
        typeof(CertificateVerificationDto).GetProperties().Select(property => property.Name)
            .ShouldBe(["HolderName", "CourseTitle", "IssuedOn", "ValidUntil", "Validity"], ignoreOrder: true);
    }

    [Fact]
    public void The_answer_travels_as_json_with_days_and_the_validity_by_name()
    {
        var verified = IssuedCertificates.Yearly().ToVerificationDto(CalendarDate.From(new DateOnly(2028, 3, 2)));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(verified, Web));

        json.RootElement.GetProperty("holderName").GetString().ShouldBe("Anna Berger");
        json.RootElement.GetProperty("courseTitle").GetString().ShouldBe("Hygiene 2027");
        json.RootElement.GetProperty("issuedOn").GetString().ShouldBe("2027-03-02");
        json.RootElement.GetProperty("validUntil").GetString().ShouldBe("2028-03-02");
        json.RootElement.GetProperty("validity").GetString().ShouldBe("Valid");
        json.RootElement.EnumerateObject().Count().ShouldBe(5);
    }

    [Theory]
    [InlineData(Validity.Valid, CertificateValidity.Valid)]
    [InlineData(Validity.Expired, CertificateValidity.Expired)]
    public void Each_validity_maps_to_its_name(Validity validity, CertificateValidity expected)
    {
        validity.ToDto().ShouldBe(expected);
    }

    [Fact]
    public void An_undefined_validity_is_unreachable()
    {
        Should.Throw<UnreachableException>(() => ((Validity)99).ToDto());
    }
}
