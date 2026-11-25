using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The pharmacy's retention (#53, docs/adr/0034; its own period, #391) in PostgreSQL: <see cref="CertificateStore.IssuedInPharmacyAsync"/>
/// keeps a certificate while <c>(issued_at AT TIME ZONE 'UTC') + make_interval(years =&gt; n)</c> lies after the instant
/// it is asked at, which must count the n years as <see cref="Certificate.IsKeptByPharmaciesAt"/> does with
/// <see cref="DateTimeOffset.AddYears"/>, or a certificate would leave the export and come back. Around 29 February a
/// bound of <c>issued_at &gt; now - 5 years</c> would not. The cases are <c>CertificateTests</c>' own; each plants a
/// membership and a certificate in a pharmacy of its own and asks the store at a <c>keptAt</c> of the case's choosing,
/// since the service's clock cannot be set.
/// </summary>
public class CertificationRetentionTests(AspireFixture aspire)
{
    [Theory]
    [InlineData(5, "2028-02-29T01:00:00Z", "2033-02-28T00:59:59.999999Z", true)]
    [InlineData(5, "2028-02-29T01:00:00Z", "2033-02-28T01:00:00Z", false)]
    [InlineData(5, "2028-02-28T13:00:00Z", "2033-02-28T12:00:00Z", true)] // issued earlier, kept longer
    [InlineData(5, "2028-02-29T01:00:00Z", "2033-02-28T12:00:00Z", false)]
    [InlineData(5, "2028-02-29T23:30:00Z", "2033-02-28T23:29:59.999999Z", true)] // 1 March 00:30 in Germany
    [InlineData(5, "2028-02-29T23:30:00Z", "2033-02-28T23:30:00Z", false)]
    [InlineData(5, "2027-02-28T23:30:00Z", "2032-02-29T00:00:00Z", false)] // on 29 February, 28 February is past
    [InlineData(5, "2027-03-01T00:30:00Z", "2032-02-29T23:59:59Z", true)]
    [InlineData(4, "2028-02-29T01:00:00Z", "2032-02-29T00:59:59.999999Z", true)] // #391: a leap year on, 29 February again
    [InlineData(4, "2028-02-29T01:00:00Z", "2032-02-29T01:00:00Z", false)]
    [InlineData(7, "2028-02-29T01:00:00Z", "2035-02-28T00:59:59.999999Z", true)] // #391: no leap year on, 28 February
    [InlineData(7, "2028-02-29T01:00:00Z", "2035-02-28T01:00:00Z", false)]
    [InlineData(2, "2027-03-01T23:30:00Z", "2029-03-01T23:29:59.999999Z", true)] // #391: the shortest period
    [InlineData(2, "2027-03-01T23:30:00Z", "2029-03-01T23:30:00Z", false)]
    [InlineData(30, "2027-03-01T23:30:00Z", "2057-03-01T23:29:59.999999Z", true)] // #391: the longest period
    [InlineData(30, "2027-03-01T23:30:00Z", "2057-03-01T23:30:00Z", false)]
    public async Task Postgres_counts_the_pharmacys_years_as_the_domain_does(int years, string issuedAt, string keptAt, bool kept)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var issued = At(issuedAt);
        var asked = At(keptAt);
        var pharmacy = PharmacyId.From(Guid.CreateVersion7());
        var holder = PersonId.From(Guid.CreateVersion7());
        var keptFor = RetentionPeriod.From(years);
        var certificate = Issue(holder, issued);
        certificate.IsKeptByPharmaciesAt(asked, keptFor).ShouldBe(kept, "the case must be the domain's answer");

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
                pharmacy, Timestamp.From(issued.Value.AddDays(-1)), Timestamp.From(issued.Value.AddDays(1)), asked, keptFor, 10, cancellationToken);

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
