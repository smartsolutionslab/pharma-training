using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.PurgeErasedCertificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

/// <summary>
/// The purge of erased persons' retained certificates (#115, slice 5; the owner's decision of 25 November 2026): every
/// pass looks again at each tombstoned person who still holds a certificate and deletes, PDF first and row with the
/// commit, every certificate of theirs no covering pharmacy still keeps under its current retention period (docs/adr/0039,
/// the longest covering period wins, the default five years where a pharmacy chose none), the rule the erasure applies.
/// Each person is held from the first read of theirs to their commit, as the erasure holds them. Nothing is published.
/// </summary>
public class PurgeErasedCertificatesHandlerTests
{
    private static readonly DateTimeOffset MembershipStart = new(2027, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Issued = new(2027, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly List<string> journal = [];
    private readonly InMemoryCertificateStore certificates = new();
    private readonly InMemoryMembershipStore memberships = new();
    private readonly InMemoryPharmacyRetentionStore retention = new();
    private readonly InMemoryErasedPersonStore erased;
    private readonly RecordingBlobDeletes blobs;
    private readonly ErasureUnitOfWork unitOfWork;
    private readonly FakeTimeProvider time = new(Issued);
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());
    private readonly PersonId otherPerson = PersonId.From(Guid.CreateVersion7());
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly PharmacyId otherPharmacy = PharmacyId.From(Guid.CreateVersion7());

    public PurgeErasedCertificatesHandlerTests()
    {
        certificates.Journal = journal;
        memberships.Journal = journal;
        erased = new InMemoryErasedPersonStore(journal, certificates);
        blobs = new RecordingBlobDeletes(journal);
        unitOfWork = new ErasureUnitOfWork(certificates, memberships, erased, journal);
    }

    /// <summary>
    /// The pharmacy kept certificates ten years when the person was erased, so their seven-year-old one was retained; it
    /// has since cut its period to five, so no pharmacy keeps it any longer and the next pass deletes it, PDF first.
    /// </summary>
    [Fact]
    public async Task A_retained_certificate_is_removed_with_its_pdf_once_its_covering_pharmacys_period_ran_out()
    {
        retention.Keep(pharmacy, 10);
        Member(person, pharmacy, MembershipStart);
        var retained = Stored(Issue(person, Issued));
        erased.Tombstone(person, Issued.AddYears(6));
        retention.Keep(pharmacy, 5);
        time.SetUtcNow(Issued.AddYears(7));

        var result = await Handler().HandleAsync(new PurgeErasedCertificates(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        certificates.Stored.ShouldBeEmpty();
        blobs.Deleted.ShouldBe([CertificateDocuments.NameFor(retained.Id)]);
        journal.IndexOf("commit").ShouldBeGreaterThan(journal.IndexOf($"delete:{CertificateDocuments.NameFor(retained.Id).Value}"), "the row goes only after its PDF");
    }

    /// <summary>A certificate whose PDF is still pending has its derived name deleted all the same: a render in flight may write it.</summary>
    [Fact]
    public async Task A_certificate_without_a_stored_pdf_has_its_derived_name_deleted_too()
    {
        Member(person, pharmacy, MembershipStart);
        var pending = Stored(Issue(person, Issued), withDocument: false);
        erased.Tombstone(person, Issued.AddYears(4));
        time.SetUtcNow(Issued.AddYears(7));

        await Handler().HandleAsync(new PurgeErasedCertificates(), TestContext.Current.CancellationToken);

        certificates.Stored.ShouldBeEmpty();
        blobs.Deleted.ShouldBe([CertificateDocuments.NameFor(pending.Id)]);
    }

    /// <summary>
    /// The person was a member of two pharmacies when it was issued: one keeps five years, the other ten. Seven years on,
    /// the second still has it in its view, so it stays; ten years on, neither has, so it goes.
    /// </summary>
    [Theory]
    [InlineData(7, true)]
    [InlineData(10, false)]
    public async Task A_certificate_covered_in_two_pharmacies_stays_while_the_longer_period_keeps_it(int yearsLater, bool isKept)
    {
        retention.Keep(pharmacy, 5);
        retention.Keep(otherPharmacy, 10);
        Member(person, pharmacy, MembershipStart);
        Member(person, otherPharmacy, MembershipStart);
        var certificate = Stored(Issue(person, Issued));
        erased.Tombstone(person, Issued.AddYears(6));
        time.SetUtcNow(Issued.AddYears(yearsLater));

        await Handler().HandleAsync(new PurgeErasedCertificates(), TestContext.Current.CancellationToken);

        if (isKept)
        {
            certificates.Stored.ShouldHaveSingleItem().ShouldBeSameAs(certificate);
            blobs.Deleted.ShouldBeEmpty();
        }
        else
        {
            certificates.Stored.ShouldBeEmpty();
            blobs.Deleted.ShouldBe([CertificateDocuments.NameFor(certificate.Id)]);
        }
    }

    /// <summary>A pharmacy that chose no period keeps certificates the default five years (docs/adr/0034, 0039).</summary>
    [Theory]
    [InlineData(4, true)]
    [InlineData(6, false)]
    public async Task The_default_period_applies_where_the_covering_pharmacy_chose_none(int yearsLater, bool isKept)
    {
        Member(person, pharmacy, MembershipStart);
        Stored(Issue(person, Issued));
        erased.Tombstone(person, Issued.AddYears(3));
        time.SetUtcNow(Issued.AddYears(yearsLater));

        await Handler().HandleAsync(new PurgeErasedCertificates(), TestContext.Current.CancellationToken);

        certificates.Stored.Count.ShouldBe(isKept ? 1 : 0);
        blobs.Deleted.Count.ShouldBe(isKept ? 0 : 1);
    }

    /// <summary>
    /// The colleague was never erased: their certificate, as old as the erased person's and in the same pharmacy, is not
    /// the purge's to delete; only the tombstoned person's goes.
    /// </summary>
    [Fact]
    public async Task Only_the_certificates_of_erased_persons_are_touched()
    {
        Member(person, pharmacy, MembershipStart);
        Member(otherPerson, pharmacy, MembershipStart);
        var erasedPersons = Stored(Issue(person, Issued));
        var colleagues = Stored(Issue(otherPerson, Issued));
        erased.Tombstone(person, Issued.AddYears(4));
        time.SetUtcNow(Issued.AddYears(7));

        await Handler().HandleAsync(new PurgeErasedCertificates(), TestContext.Current.CancellationToken);

        certificates.Stored.ShouldHaveSingleItem().ShouldBeSameAs(colleagues);
        blobs.Deleted.ShouldBe([CertificateDocuments.NameFor(erasedPersons.Id)]);
    }

    /// <summary>
    /// Two erased persons hold certificates: the first's no pharmacy keeps, the second's a ten-year pharmacy still does.
    /// Each is held before anything of theirs is read and committed before the next is held, the second too, though
    /// nothing of theirs goes: the commit also ends the hold's transaction.
    /// </summary>
    [Fact]
    public async Task Each_erased_holder_is_held_before_anything_of_theirs_is_read_and_committed_on_their_own()
    {
        retention.Keep(otherPharmacy, 10);
        Member(person, pharmacy, MembershipStart);
        Member(otherPerson, otherPharmacy, MembershipStart);
        Stored(Issue(person, Issued));
        var kept = Stored(Issue(otherPerson, Issued));
        erased.Tombstone(person, Issued.AddYears(4));
        erased.Tombstone(otherPerson, Issued.AddYears(4));
        time.SetUtcNow(Issued.AddYears(7));

        await Handler().HandleAsync(new PurgeErasedCertificates(), TestContext.Current.CancellationToken);

        var passes = PassesIn(journal);
        passes.Count.ShouldBe(2, string.Join(", ", journal));
        foreach (var pass in passes)
        {
            pass[0].ShouldBe("hold", "a membership, passed test or render running at the same time must not slip in between the reads and the deletes");
            pass.ShouldContain("read-certificates");
            pass.ShouldContain("read-memberships");
            pass[^1].ShouldBe("commit");
        }

        unitOfWork.Commits.ShouldBe(2);
        certificates.Stored.ShouldHaveSingleItem().ShouldBeSameAs(kept);
    }

    [Fact]
    public async Task A_pass_with_nothing_to_purge_succeeds_and_changes_nothing()
    {
        Member(otherPerson, pharmacy, MembershipStart);
        var colleagues = Stored(Issue(otherPerson, Issued));
        erased.Tombstone(person, Issued.AddYears(4));
        time.SetUtcNow(Issued.AddYears(7));

        var result = await Handler().HandleAsync(new PurgeErasedCertificates(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        unitOfWork.Commits.ShouldBe(0);
        journal.ShouldNotContain("hold");
        blobs.Deleted.ShouldBeEmpty();
        certificates.Stored.ShouldHaveSingleItem().ShouldBeSameAs(colleagues);
    }

    [Fact]
    public async Task The_purge_publishes_nothing()
    {
        Member(person, pharmacy, MembershipStart);
        Stored(Issue(person, Issued));
        erased.Tombstone(person, Issued.AddYears(4));
        time.SetUtcNow(Issued.AddYears(7));

        var result = await Handler().HandleAsync(new PurgeErasedCertificates(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        certificates.Stored.ShouldBeEmpty();
        certificates.Published.ShouldBeEmpty();
    }

    private static Certificate Issue(PersonId holder, DateTimeOffset issuedAt) => TestCertificates.Issued(holder, "Hygiene 2027", issuedAt);

    /// <summary>The journal after the read of the erased holders, cut into one list per person, each ending at its commit.</summary>
    private static List<List<string>> PassesIn(List<string> entries)
    {
        entries.ShouldContain("read-holders");
        List<List<string>> passes = [];
        List<string> current = [];
        foreach (var entry in entries.Skip(entries.IndexOf("read-holders") + 1))
        {
            current.Add(entry);
            if (entry == "commit")
            {
                passes.Add(current);
                current = [];
            }
        }

        current.ShouldBeEmpty("nothing is read or deleted after the last commit");
        return passes;
    }

    private PurgeErasedCertificatesHandler Handler() => new(erased, certificates, memberships, retention, blobs, unitOfWork, time);

    /// <summary>A membership of the person in the pharmacy from the instant on, stored as if committed earlier.</summary>
    private void Member(PersonId of, PharmacyId at, DateTimeOffset startedAt) =>
        memberships.Memberships.Add(Membership.Started(MembershipId.From(Guid.CreateVersion7()), of, at, HolderName.From("Anna Berger"), MembershipRole.Employee, Timestamp.From(startedAt)));

    /// <summary>The certificate stored as if committed earlier, its PDF stored at its derived name or still pending.</summary>
    private Certificate Stored(Certificate certificate, bool withDocument = true)
    {
        if (withDocument)
        {
            certificate.AttachDocument(DocumentReference.From(CertificateDocuments.NameFor(certificate.Id).Value));
        }

        certificates.Stored.Add(certificate);
        return certificate;
    }
}
