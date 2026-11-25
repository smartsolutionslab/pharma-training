using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

/// <summary>
/// Organisation announced a person's erasure (#115, slice 4): Certification holds the person and tombstones them (an
/// unknown one too), removes the certificates no pharmacy keeps any longer, PDFs first and rows at the commit, keeps the
/// retained ones as they are, name and PDF included, forgets the holder's name on every membership of theirs, purges the
/// stored membership messages and announces <see cref="CertificationErasureCompletedV1"/>. A certificate is retained
/// while some membership of the person covers its issue (start inclusive, end exclusive, docs/adr/0032) and pharmacies
/// still keep it at Certification's clock (<see cref="Certificate.IsKeptByPharmaciesAt"/>, docs/adr/0034); every other
/// one is removed. It is idempotent by state: a redelivery changes nothing but announces again.
/// </summary>
public class PersonErasureRequestedV1ConsumerTests
{
    private static readonly DateTimeOffset MembershipStart = new(2027, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Issued = new(2027, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AYearLater = Issued.AddYears(1);

    private readonly List<string> journal = [];
    private readonly InMemoryCertificateStore certificates = new();
    private readonly InMemoryMembershipStore memberships = new();
    private readonly InMemoryPharmacyRetentionStore retention = new();
    private readonly InMemoryErasedPersonStore erased;
    private readonly RecordingBlobDeletes blobs;
    private readonly ErasureUnitOfWork unitOfWork;
    private readonly IPersonalMessagePurge purge = Substitute.For<IPersonalMessagePurge>();
    private readonly FakeTimeProvider time = new(AYearLater);
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());
    private readonly PersonId colleague = PersonId.From(Guid.CreateVersion7());
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly PharmacyId otherPharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly Guid erasure = Guid.CreateVersion7();

    public PersonErasureRequestedV1ConsumerTests()
    {
        certificates.Journal = journal;
        memberships.Journal = journal;
        erased = new InMemoryErasedPersonStore(journal);
        blobs = new RecordingBlobDeletes(journal);
        unitOfWork = new ErasureUnitOfWork(certificates, memberships, erased, journal);
    }

    /// <summary>Four years and 364 days, and a second short of five years, after the issue: pharmacies still keep it.</summary>
    [Theory]
    [InlineData(4, 364, 0)]
    [InlineData(5, 0, -1)]
    public async Task A_certificate_inside_a_membership_that_pharmacies_still_keep_is_retained_with_its_name_and_pdf(int years, int days, int seconds)
    {
        Member(person, pharmacy, MembershipStart);
        var retained = Stored(Issue(person, Issued), withDocument: true);
        time.SetUtcNow(Issued.AddYears(years).AddDays(days).AddSeconds(seconds));

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        certificates.Stored.ShouldHaveSingleItem().ShouldBeSameAs(retained);
        retained.HolderName.ShouldBe(HolderName.From("Anna Berger"), "a retained certificate keeps its holder's name (decision 3)");
        retained.Document.ShouldBe(DocumentState.Stored);
        retained.DocumentLocation.ShouldBe(DocumentReference.From(CertificateDocuments.NameFor(retained.Id).Value));
        blobs.Deleted.ShouldBeEmpty("a retained certificate keeps its PDF");
    }

    /// <summary>Five years after the issue to the instant pharmacies no longer keep it (docs/adr/0034), nor a day later.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_certificate_five_years_old_or_older_is_removed_with_its_pdf_though_a_membership_covers_it(int daysAfterFiveYears)
    {
        Member(person, pharmacy, MembershipStart);
        var removed = Stored(Issue(person, Issued), withDocument: true);
        time.SetUtcNow(Issued.AddYears(5).AddDays(daysAfterFiveYears));

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        certificates.Stored.ShouldBeEmpty();
        blobs.Deleted.ShouldBe([CertificateDocuments.NameFor(removed.Id)]);
    }

    /// <summary>
    /// The person worked in one pharmacy from 1 January to 1 February 2027 and in another from 1 April on. Certificates
    /// issued at the first start and during the second are retained; one issued before any membership, one at the first
    /// end (end exclusive) and one between the two are removed, though all are less than five years old.
    /// </summary>
    [Fact]
    public async Task A_certificate_issued_outside_every_membership_of_the_person_is_removed_however_recent()
    {
        var firstEnd = new DateTimeOffset(2027, 2, 1, 8, 0, 0, TimeSpan.Zero);
        var first = Member(person, pharmacy, MembershipStart);
        memberships.Endings.Add(MembershipEnding.Of(first.Id, person, pharmacy, Timestamp.From(firstEnd)));
        Member(person, otherPharmacy, new DateTimeOffset(2027, 4, 1, 8, 0, 0, TimeSpan.Zero));
        var beforeAny = Stored(Issue(person, new DateTimeOffset(2026, 12, 15, 10, 0, 0, TimeSpan.Zero)), withDocument: true);
        var atFirstStart = Stored(Issue(person, MembershipStart), withDocument: true);
        var atFirstEnd = Stored(Issue(person, firstEnd), withDocument: true);
        var between = Stored(Issue(person, new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.Zero)), withDocument: true);
        var duringSecond = Stored(Issue(person, new DateTimeOffset(2027, 5, 1, 10, 0, 0, TimeSpan.Zero)), withDocument: true);
        time.SetUtcNow(new DateTimeOffset(2028, 6, 1, 0, 0, 0, TimeSpan.Zero));

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        certificates.Stored.Select(certificate => certificate.Id).ShouldBe([atFirstStart.Id, duringSecond.Id], ignoreOrder: true);
        blobs.Deleted.ShouldBe([CertificateDocuments.NameFor(beforeAny.Id), CertificateDocuments.NameFor(atFirstEnd.Id), CertificateDocuments.NameFor(between.Id)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_person_without_any_membership_has_every_certificate_removed()
    {
        var certificate = Stored(Issue(person, Issued), withDocument: true);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        certificates.Stored.ShouldBeEmpty();
        blobs.Deleted.ShouldBe([CertificateDocuments.NameFor(certificate.Id)]);
    }

    /// <summary>
    /// Blobs first, so a run that fails between the two leaves no PDF without its row: the retry finds the rows still
    /// there and deletes what is left. A certificate whose PDF is still pending has its derived name deleted all the same,
    /// since a render in flight may write it.
    /// </summary>
    [Fact]
    public async Task The_pdfs_are_deleted_before_the_rows_are_committed_away_whether_or_not_one_was_stored()
    {
        var stored = Stored(Issue(person, Issued), withDocument: true);
        var pending = Stored(Issue(person, Issued.AddDays(1)), withDocument: false);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        var deletes = journal.Where(entry => entry.StartsWith("delete:", StringComparison.Ordinal)).ToList();
        deletes.ShouldBe([$"delete:{CertificateDocuments.NameFor(stored.Id).Value}", $"delete:{CertificateDocuments.NameFor(pending.Id).Value}"], ignoreOrder: true);
        journal.IndexOf("commit").ShouldBeGreaterThan(journal.FindLastIndex(entry => entry.StartsWith("delete:", StringComparison.Ordinal)), "the rows go only after every PDF");
        journal.Count(entry => entry == "commit").ShouldBe(1);
        certificates.Stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_holder_name_is_erased_from_every_membership_of_the_person_whose_rows_stay_and_kept_for_everyone_else()
    {
        var ended = Member(person, pharmacy, MembershipStart);
        memberships.Endings.Add(MembershipEnding.Of(ended.Id, person, pharmacy, Timestamp.From(MembershipStart.AddMonths(1))));
        Member(person, otherPharmacy, MembershipStart.AddMonths(2));
        var colleagues = Membership.Started(MembershipId.From(Guid.CreateVersion7()), colleague, pharmacy, HolderName.From("Grace Hopper"), MembershipRole.Employee, Timestamp.From(MembershipStart));
        memberships.Memberships.Add(colleagues);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        var persons = memberships.Memberships.Where(membership => membership.Person == person).ToList();
        persons.Count.ShouldBe(2, "the rows stay: their periods choose the retained certificates for the export");
        persons.ShouldAllBe(membership => membership.HolderName == null);
        memberships.Endings.ShouldHaveSingleItem();
        colleagues.HolderName.ShouldBe(HolderName.From("Grace Hopper"));
    }

    [Fact]
    public async Task The_person_is_tombstoned_at_certifications_clock()
    {
        Member(person, pharmacy, MembershipStart);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        var tombstone = erased.Stored.ShouldHaveSingleItem();
        tombstone.Person.ShouldBe(person);
        tombstone.ErasedAt.ShouldBe(Timestamp.From(AYearLater));
    }

    [Fact]
    public async Task The_erasure_holds_the_person_before_it_reads_anything_of_theirs()
    {
        Member(person, pharmacy, MembershipStart);
        Stored(Issue(person, Issued), withDocument: true);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        journal.ShouldNotBeEmpty();
        journal[0].ShouldBe("hold", "a membership or passed test running at the same time must not slip a name or a certificate between this read and the tombstone");
        journal.ShouldContain("read-certificates");
        journal.ShouldContain("read-memberships");
    }

    /// <summary>The membership messages carry the person's name; <c>TestPassedV1</c> carries their id only, so it is not purged.</summary>
    [Fact]
    public async Task The_stored_membership_messages_holding_the_persons_id_are_purged()
    {
        Member(person, pharmacy, MembershipStart);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        await purge.Received(1).PurgeAsync(
            person.Value,
            Arg.Is<IReadOnlyCollection<Type>>(types => types.Count == 2 && types.Contains(typeof(MembershipStartedV1)) && types.Contains(typeof(MembershipChangedV1))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Certifications_completion_is_announced_with_the_erasure_once_committed()
    {
        Member(person, pharmacy, MembershipStart);
        Stored(Issue(person, Issued), withDocument: true);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        certificates.Published.ShouldHaveSingleItem().ShouldBe(new CertificationErasureCompletedV1(erasure));
        unitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_person_is_tombstoned_and_still_completes_and_changes_no_one_else()
    {
        var colleagues = Membership.Started(MembershipId.From(Guid.CreateVersion7()), colleague, pharmacy, HolderName.From("Grace Hopper"), MembershipRole.Employee, Timestamp.From(MembershipStart));
        memberships.Memberships.Add(colleagues);
        var colleaguesCertificate = Stored(Issue(colleague, new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)), withDocument: true);

        await Consumer().HandleAsync(Requested(person), TestContext.Current.CancellationToken);

        erased.Stored.ShouldHaveSingleItem().Person.ShouldBe(person, "a membership or passed test arriving late must find the tombstone");
        certificates.Published.ShouldHaveSingleItem().ShouldBe(new CertificationErasureCompletedV1(erasure));
        certificates.Stored.ShouldHaveSingleItem().ShouldBeSameAs(colleaguesCertificate);
        colleagues.HolderName.ShouldBe(HolderName.From("Grace Hopper"));
        blobs.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_redelivery_changes_nothing_but_announces_the_completion_again()
    {
        Member(person, pharmacy, MembershipStart);
        var retained = Stored(Issue(person, Issued), withDocument: true);
        Stored(Issue(person, MembershipStart.AddDays(-1)), withDocument: true);
        var requested = Requested(person);
        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);
        var deletedFirst = blobs.Deleted.Count;
        time.Advance(TimeSpan.FromMinutes(5));

        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);

        erased.Stored.ShouldHaveSingleItem().ErasedAt.ShouldBe(Timestamp.From(AYearLater), "the first tombstone stays as it was");
        certificates.Stored.ShouldHaveSingleItem().ShouldBeSameAs(retained);
        retained.HolderName.ShouldBe(HolderName.From("Anna Berger"));
        deletedFirst.ShouldBe(1);
        blobs.Deleted.Count.ShouldBe(deletedFirst, "nothing is left to delete");
        memberships.Memberships.ShouldHaveSingleItem().HolderName.ShouldBeNull();
        certificates.Published.Count.ShouldBe(2);
        certificates.Published.ShouldAllBe(published => published.Equals(new CertificationErasureCompletedV1(erasure)));
    }

    [Theory]
    [InlineData(nameof(PersonErasureRequestedV1.ErasureId))]
    [InlineData(nameof(PersonErasureRequestedV1.PersonId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_done(string field)
    {
        var requested = new PersonErasureRequestedV1(
            field == nameof(PersonErasureRequestedV1.ErasureId) ? Guid.Empty : erasure,
            field == nameof(PersonErasureRequestedV1.PersonId) ? Guid.Empty : person.Value);

        var refused = await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(requested, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        NothingDone();
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_done()
    {
        await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(null!, TestContext.Current.CancellationToken));

        NothingDone();
    }

    private static Certificate Issue(PersonId holder, DateTimeOffset issuedAt) => TestCertificates.Issued(holder, "Hygiene 2027", issuedAt);

    private PersonErasureRequestedV1Consumer Consumer() => new(certificates, memberships, retention, erased, blobs, purge, unitOfWork, time);

    private PersonErasureRequestedV1 Requested(PersonId erasedPerson) => new(erasure, erasedPerson.Value);

    /// <summary>A membership of the person in the pharmacy from the instant on, stored as if committed earlier.</summary>
    private Membership Member(PersonId of, PharmacyId at, DateTimeOffset startedAt)
    {
        var membership = Membership.Started(MembershipId.From(Guid.CreateVersion7()), of, at, HolderName.From("Anna Berger"), MembershipRole.Employee, Timestamp.From(startedAt));
        memberships.Memberships.Add(membership);
        return membership;
    }

    /// <summary>The certificate stored as if committed earlier, its PDF stored at its derived name or still pending.</summary>
    private Certificate Stored(Certificate certificate, bool withDocument)
    {
        if (withDocument)
        {
            certificate.AttachDocument(DocumentReference.From(CertificateDocuments.NameFor(certificate.Id).Value));
        }

        certificates.Stored.Add(certificate);
        return certificate;
    }

    private void NothingDone()
    {
        unitOfWork.Commits.ShouldBe(0);
        certificates.Published.ShouldBeEmpty();
        blobs.Deleted.ShouldBeEmpty();
        erased.Stored.ShouldBeEmpty();
    }
}
