using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.ErasedPersons;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>
/// Erases a person in Certification (#115) and announces it as <see cref="CertificationErasureCompletedV1"/>. It holds
/// the person (<see cref="IErasedPersonStore.HoldAsync"/>), tombstones them (an unknown person too, so a membership or
/// passed test arriving late is dropped), and splits their certificates at Certification's clock: a certificate is
/// <b>retained</b> while some membership of theirs covers its issue (start inclusive, end exclusive, docs/adr/0032) and
/// pharmacies still keep it (<see cref="Certificate.IsKeptByPharmaciesAt"/>, docs/adr/0034), so it stays in a pharmacy's
/// view and export and answers the public verification, name and PDF included; every other one is <b>removed</b>. The
/// PDFs of the removed ones are deleted first, each by its derived name whether or not one was stored (a render in
/// flight may write it), then their rows go with the commit, so a run that fails between the two leaves no PDF without
/// its row and the retry deletes what is left. The holder's name is forgotten on every membership of theirs, whose rows
/// stay: their periods choose the retained certificates. Before the commit it purges the stored membership messages
/// that hold their id; the purge runs on a connection of its own and commits by itself, so a redelivery purges again.
/// <para>
/// The person is held from the first read to the commit, as the membership, passed-test and document consumers hold
/// them, so none of them can slip a name, a certificate or a PDF between these reads and the tombstone.
/// </para>
/// <para>
/// It is idempotent by state, not by <c>Metadata.EventIdentifier</c>: a redelivery waits for the first under the hold,
/// keeps its tombstone, finds the removed certificates and the names gone, and announces the completion again, which
/// Organisation records once.
/// </para>
/// </summary>
public sealed class PersonErasureRequestedV1Consumer(
    ICertificateStore certificates,
    IMembershipStore memberships,
    IPharmacyRetentionStore retention,
    IErasedPersonStore erased,
    IBlobDeletes blobs,
    IPersonalMessagePurge purge,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider time)
{
    /// <summary>The messages Certification stores that carry a person's name; <c>TestPassedV1</c> carries the id only.</summary>
    private static readonly IReadOnlyCollection<Type> PersonalMessages = [typeof(MembershipStartedV1), typeof(MembershipChangedV1)];

    private readonly ICertificateStore certificates = certificates;
    private readonly IMembershipStore memberships = memberships;
    private readonly IPharmacyRetentionStore retention = retention;
    private readonly IErasedPersonStore erased = erased;
    private readonly IBlobDeletes blobs = blobs;
    private readonly IPersonalMessagePurge purge = purge;
    private readonly ICertificationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public async Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.ErasureId, nameof(message.ErasureId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();

        var person = PersonId.From(message.PersonId);
        var now = Timestamp.From(time.GetUtcNow());

        // Held before anything of the person is read, so a membership, passed test or render running at the same time
        // either commits before these reads (and is erased here) or waits and then finds the tombstone.
        await erased.HoldAsync(person, cancellationToken);
        if (!await erased.IsErasedAsync(person, cancellationToken))
        {
            erased.Add(ErasedPerson.Of(person, now));
        }

        var held = await certificates.OfHolderAsync(person, cancellationToken);
        var persons = await memberships.OfPersonAsync(person, cancellationToken);
        var endings = await memberships.EndingsOfPersonAsync(person, cancellationToken);

        var removed = held.Where(certificate => !IsRetained(certificate, persons, endings, now)).ToList();
        foreach (var certificate in removed)
        {
            // Blobs first: a failure here leaves every row, so the retry finds what is left to delete.
            await blobs.DeleteAsync(CertificateDocuments.NameFor(certificate.Id), cancellationToken);
        }

        foreach (var certificate in removed)
        {
            certificates.Remove(certificate);
        }

        foreach (var membership in persons)
        {
            membership.EraseHolderName();
        }

        await purge.PurgeAsync(person.Value, PersonalMessages, cancellationToken);

        await unitOfWork.PublishAsync(new CertificationErasureCompletedV1(message.ErasureId));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Whether a pharmacy still has the certificate in its view at <paramref name="now"/>: pharmacies keep it, and some
    /// membership of its holder covers its issue (the rule <see cref="ICertificateStore.IssuedInPharmacyAsync"/> applies).
    /// </summary>
    private static bool IsRetained(Certificate certificate, IReadOnlyList<Membership> persons, IReadOnlyList<MembershipEnding> endings, Timestamp now) =>
        certificate.IsKeptByPharmaciesAt(now, RetentionPeriod.Default)
        && persons.Any(membership => membership.Covers(certificate.IssuedAt, endings.FirstOrDefault(ending => ending.Id == membership.Id)));
}
