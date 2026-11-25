using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Erasures;

/// <summary>
/// Which certificates of an erased person go, and how they go (#115): the rule the erasure applies to every certificate
/// of theirs and the purge applies again to those it retained (slice 5, the owner's decision of 25 November 2026).
/// </summary>
public static class ErasedCertificates
{
    /// <summary>
    /// The certificates of <paramref name="held"/> no pharmacy still has in its view at <paramref name="now"/>. A
    /// certificate is kept while some membership of its holder covers its issue (start inclusive, end exclusive,
    /// docs/adr/0032) and that membership's pharmacy still keeps it for the period it chose now, the default where it
    /// chose none (<see cref="Certificate.IsKeptByPharmaciesAt"/>, docs/adr/0034, 0039), the rules
    /// <see cref="ICertificateStore.IssuedInPharmacyAsync"/> applies; the longest covering period wins.
    /// </summary>
    public static async Task<IReadOnlyList<Certificate>> NoLongerKeptAsync(
        IReadOnlyList<Certificate> held,
        IReadOnlyList<Membership> persons,
        IReadOnlyList<MembershipEnding> endings,
        IPharmacyRetentionStore retention,
        Timestamp now,
        CancellationToken cancellationToken)
    {
        Ensure.That(held).IsNotNull();
        Ensure.That(persons).IsNotNull();
        Ensure.That(endings).IsNotNull();
        Ensure.That(retention).IsNotNull();
        Ensure.That(now).IsNotNull();

        var keptFor = new Dictionary<PharmacyId, RetentionPeriod>();
        foreach (var pharmacy in persons.Select(membership => membership.Pharmacy).Distinct())
        {
            keptFor[pharmacy] = (await retention.FindAsync(pharmacy, cancellationToken)).Match(stored => stored.Period, () => RetentionPeriod.Default);
        }

        return [.. held.Where(certificate => !IsKept(certificate, persons, endings, keptFor, now))];
    }

    /// <summary>
    /// Deletes the PDFs of <paramref name="removed"/> first, each by its derived name whether or not one was stored (a
    /// render in flight may write it), then removes their rows, which go with the unit of work's commit: a run that fails
    /// between the two leaves no PDF without its row, and the retry deletes what is left.
    /// </summary>
    public static async Task RemoveAsync(
        IReadOnlyList<Certificate> removed,
        IBlobDeletes blobs,
        ICertificateStore certificates,
        CancellationToken cancellationToken)
    {
        Ensure.That(removed).IsNotNull();
        Ensure.That(blobs).IsNotNull();
        Ensure.That(certificates).IsNotNull();

        foreach (var certificate in removed)
        {
            // Blobs first: a failure here leaves every row, so the retry finds what is left to delete.
            await blobs.DeleteAsync(CertificateDocuments.NameFor(certificate.Id), cancellationToken);
        }

        foreach (var certificate in removed)
        {
            certificates.Remove(certificate);
        }
    }

    private static bool IsKept(
        Certificate certificate,
        IReadOnlyList<Membership> persons,
        IReadOnlyList<MembershipEnding> endings,
        Dictionary<PharmacyId, RetentionPeriod> keptFor,
        Timestamp now) =>
        persons.Any(membership =>
            membership.Covers(certificate.IssuedAt, endings.FirstOrDefault(ending => ending.Id == membership.Id))
            && certificate.IsKeptByPharmaciesAt(now, keptFor[membership.Pharmacy]));
}
