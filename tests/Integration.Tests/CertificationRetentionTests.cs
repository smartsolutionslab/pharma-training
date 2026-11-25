using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The pharmacy's retention (#53, docs/adr/0034) in PostgreSQL: <see cref="CertificateStore.IssuedInPharmacyAsync"/>
/// keeps a certificate while <c>(issued_at AT TIME ZONE 'UTC') + make_interval(years =&gt; 5)</c> lies after the instant
/// it is asked at, which must count the five years as <see cref="Certificate.IsKeptByPharmaciesAt"/> does with
/// <see cref="DateTimeOffset.AddYears"/>, or a certificate would leave the export and come back. Around 29 February a
/// bound of <c>issued_at &gt; now - 5 years</c> would not. The cases are <c>CertificateTests</c>' own; each plants a
/// membership and a certificate in a pharmacy of its own and asks the store at a <c>keptAt</c> of the case's choosing,
/// since the service's clock cannot be set.
/// </summary>
public class CertificationRetentionTests(AspireFixture aspire)
{
    [Theory]
    [InlineData("2028-02-29T01:00:00Z", "2033-02-28T00:59:59.999999Z", true)]
    [InlineData("2028-02-29T01:00:00Z", "2033-02-28T01:00:00Z", false)]
    [InlineData("2028-02-28T13:00:00Z", "2033-02-28T12:00:00Z", true)] // issued earlier, kept longer
    [InlineData("2028-02-29T01:00:00Z", "2033-02-28T12:00:00Z", false)]
    [InlineData("2028-02-29T23:30:00Z", "2033-02-28T23:29:59.999999Z", true)] // 1 March 00:30 in Germany
    [InlineData("2028-02-29T23:30:00Z", "2033-02-28T23:30:00Z", false)]
    [InlineData("2027-02-28T23:30:00Z", "2032-02-29T00:00:00Z", false)] // on 29 February, 28 February is past
    [InlineData("2027-03-01T00:30:00Z", "2032-02-29T23:59:59Z", true)]
    public async Task Postgres_counts_five_years_around_29_February_as_the_domain_does(string issuedAt, string keptAt, bool kept)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var issued = At(issuedAt);
        var asked = At(keptAt);
        var pharmacy = PharmacyId.From(Guid.CreateVersion7());
        var holder = PersonId.From(Guid.CreateVersion7());
        var certificate = Issue(holder, issued);
        certificate.IsKeptByPharmaciesAt(asked).ShouldBe(kept, "the case must be the domain's answer");

        // The domain events of the planted certificate go to no reaction: these services register none.
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var connectionString = await CertificationDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var options = new DbContextOptionsBuilder<CertificationDbContext>().UseNpgsql(connectionString).Options;
        await using (var context = new CertificationDbContext(options, services))
        {
            context.Add(Membership.Started(MembershipId.From(Guid.CreateVersion7()), holder, pharmacy, HolderName.From("Anna Berger"), MembershipRole.Employee, Timestamp.From(issued.Value.AddYears(-1))));
            context.Add(certificate);
            await context.SaveChangesAsync(cancellationToken);
        }

        await using (var context = new CertificationDbContext(options, services))
        {
            var found = await new CertificateStore(context).IssuedInPharmacyAsync(
                pharmacy, Timestamp.From(issued.Value.AddDays(-1)), Timestamp.From(issued.Value.AddDays(1)), asked, 10, cancellationToken);

            CertificateId[] expected = kept ? [certificate.Id] : [];
            found.Select(item => item.Id).ShouldBe(expected);
        }
    }

    private static Timestamp At(string instant) =>
        Timestamp.From(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));

    private static Certificate Issue(PersonId holder, Timestamp issuedAt) =>
        Certificate.Issue(
            CertificateId.From(Guid.CreateVersion7()),
            AttemptId.From(Guid.CreateVersion7()),
            holder,
            HolderName.From("Anna Berger"),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            CourseTitle.From("Hygiene 2027"),
            CourseVersionNumber.From(3),
            RecurrenceInterval.Yearly,
            ScorePercent.From(90),
            VerificationCode.From(Guid.CreateVersion7().ToString("N")),
            issuedAt);
}
