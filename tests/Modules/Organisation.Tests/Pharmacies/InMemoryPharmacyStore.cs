using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// The pharmacies table in memory, with the invitation recipients and senders tables (#265), and the module's unit of
/// work over them: what is added is stored, and what is published is sent, only once committed. <see cref="LoseNextCommit"/> makes the next <see cref="TryCommitAsync"/>
/// lose a race, as a save does whose pharmacy another save changed since it was loaded; <see cref="FailNextCommit"/>
/// makes the next <see cref="CommitAsync"/> throw, as <c>CommitAsync</c> lets a lost race through;
/// <see cref="FailNextFind"/> makes the next no-tracking read (<see cref="FindAsync"/>) throw, as a dropped connection
/// does, while <see cref="LoadAsync"/> still loads.
/// </summary>
internal sealed class InMemoryPharmacyStore : IPharmacyStore, IInvitationRecipientStore, IInvitationSenderStore, IOrganisationUnitOfWork
{
    private static readonly LostRace LockTimedOut = new SaveDeadlocked("55P03");

    private readonly List<Pharmacy> added = [];
    private readonly List<InvitationRecipient> addedRecipients = [];
    private readonly List<InvitationSender> addedSenders = [];
    private readonly List<IntegrationEvent> queued = [];

    private bool loseNextCommit;
    private bool timeOutNextRecipientLock;
    private bool timeOutNextSenderLock;

    private LostRace how = new VersionMovedOn();
    private Exception? failNextCommit;
    private Exception? failNextFind;
    private Action? onCommit;

    public List<Pharmacy> Stored { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public List<InvitationRecipient> StoredRecipients { get; } = [];

    public List<InvitationSender> StoredSenders { get; } = [];

    public int Commits { get; private set; }

    public void Add(Pharmacy pharmacy) => added.Add(pharmacy);

    /// <summary>
    /// The stored recipient itself, so what a command changes on it is what the next read finds once committed; an
    /// address with none gets a new one, <see cref="InvitationRecipient.NotYetMailed"/>, stored only once committed, as
    /// the database's row inserted in the command's transaction is. After <see cref="TimeOutNextRecipientLock"/> it
    /// answers a lock timeout instead and discards what the command did, as its transaction is ended.
    /// </summary>
    public Task<Result<InvitationRecipient, LostRace>> LoadForMailAsync(EmailAddress address, CancellationToken cancellationToken)
    {
        if (timeOutNextRecipientLock)
        {
            timeOutNextRecipientLock = false;
            Discard();
            return Task.FromResult<Result<InvitationRecipient, LostRace>>(Failure(LockTimedOut));
        }

        var id = InvitationRecipientId.For(address);
        if ((StoredRecipients.SingleOrDefault(recipient => recipient.Id == id) ?? addedRecipients.SingleOrDefault(recipient => recipient.Id == id)) is { } known)
        {
            return Task.FromResult<Result<InvitationRecipient, LostRace>>(Success(known));
        }

        var created = InvitationRecipient.NotYetMailed(address);
        addedRecipients.Add(created);
        return Task.FromResult<Result<InvitationRecipient, LostRace>>(Success(created));
    }

    /// <summary>
    /// The stored sender itself, so what a command changes on it is what the next read finds once committed; a sender
    /// with none gets a new one, <see cref="InvitationSender.NotYetMailed"/>, stored only once committed, as the database's
    /// row inserted in the command's transaction is. After <see cref="TimeOutNextSenderLock"/> it answers a lock timeout
    /// instead and discards what the command did, as its transaction is ended.
    /// </summary>
    public Task<Result<InvitationSender, LostRace>> LoadForMailAsync(PersonId id, Timestamp now, CancellationToken cancellationToken)
    {
        if (timeOutNextSenderLock)
        {
            timeOutNextSenderLock = false;
            Discard();
            return Task.FromResult<Result<InvitationSender, LostRace>>(Failure(LockTimedOut));
        }

        if ((StoredSenders.SingleOrDefault(sender => sender.Id == id) ?? addedSenders.SingleOrDefault(sender => sender.Id == id)) is { } known)
        {
            return Task.FromResult<Result<InvitationSender, LostRace>>(Success(known));
        }

        var created = InvitationSender.NotYetMailed(id, now);
        addedSenders.Add(created);
        return Task.FromResult<Result<InvitationSender, LostRace>>(Success(created));
    }

    /// <summary>The next recipient loaded for a mail waits for its row past the lock timeout (55P03, #375).</summary>
    public void TimeOutNextRecipientLock() => timeOutNextRecipientLock = true;

    /// <summary>The next sender loaded for a mail waits for their row past the lock timeout (55P03, #375).</summary>
    public void TimeOutNextSenderLock() => timeOutNextSenderLock = true;

    public Task<Option<Pharmacy>> FindAsync(PharmacyId id, CancellationToken cancellationToken)
    {
        if (failNextFind is { } failure)
        {
            failNextFind = null;
            return Task.FromException<Option<Pharmacy>>(failure);
        }

        return Task.FromResult(Lookup(id));
    }

    /// <summary>The stored pharmacy itself, so what a command changes on it is what the next read finds once committed.</summary>
    public Task<Option<Pharmacy>> LoadAsync(PharmacyId id, CancellationToken cancellationToken) => Task.FromResult(Lookup(id));

    /// <summary>The one stored pharmacy whose memberships hold the hash, as the database's unique index on it finds it.</summary>
    public Task<Option<Pharmacy>> LoadByInvitationAsync(InvitationTokenHash hash, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(pharmacy => pharmacy.Memberships.Any(membership => membership.InvitationLinks.Any(link => link.Hash == hash))) is { } pharmacy
            ? Option<Pharmacy>.Some(pharmacy)
            : Option<Pharmacy>.None);

    public Task<PagedResult<Pharmacy>> ListWithActiveMemberAsync(PersonId person, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        List<Pharmacy> members = [.. Stored
            .Where(pharmacy => pharmacy.Memberships.Any(membership => membership.Person == person && membership.Status == MembershipStatus.Active))
            .OrderBy(pharmacy => pharmacy.Name.Value, StringComparer.Ordinal)
            .ThenBy(pharmacy => pharmacy.Id.Value)];
        List<Pharmacy> items = [.. members.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
        return Task.FromResult(new PagedResult<Pharmacy>(items, members.Count, page.Value, pageSize.Value));
    }

    /// <summary>The pharmacy's Active memberships, by name (ordinal), then id, a page at a time, with how many it has.</summary>
    public Task<PagedResult<Membership>> ListActiveMembershipsAsync(PharmacyId pharmacy, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        List<Membership> members = [.. MembershipsOf(pharmacy, MembershipStatus.Active)
            .OrderBy(membership => membership.Name?.Value, StringComparer.Ordinal)
            .ThenBy(membership => membership.Id.Value)];
        return Task.FromResult(PageOf(members, page, pageSize));
    }

    /// <summary>The pharmacy's Invited memberships, open or run out, by expiry, then id, a page at a time, with how many it has.</summary>
    public Task<PagedResult<Membership>> ListInvitedMembershipsAsync(PharmacyId pharmacy, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        List<Membership> invited = [.. MembershipsOf(pharmacy, MembershipStatus.Invited)
            .OrderBy(membership => membership.InvitationExpiresAt)
            .ThenBy(membership => membership.Id.Value)];
        return Task.FromResult(PageOf(invited, page, pageSize));
    }

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        queued.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        if (failNextCommit is { } failure)
        {
            failNextCommit = null;
            Discard();
            throw failure;
        }

        Stored.AddRange(added);
        added.Clear();
        StoredRecipients.AddRange(addedRecipients);
        addedRecipients.Clear();
        StoredSenders.AddRange(addedSenders);
        addedSenders.Clear();
        Published.AddRange(queued);
        queued.Clear();
        Commits++;
        onCommit?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Runs the action as every <see cref="CommitAsync"/> completes, once it has stored and sent.</summary>
    public void OnCommit(Action action) => onCommit = action;

    /// <summary>The next <see cref="CommitAsync"/> throws the failure and stores and sends nothing.</summary>
    public void FailNextCommit(Exception failure) => failNextCommit = failure;

    /// <summary>The next <see cref="FindAsync"/> throws the failure; <see cref="LoadAsync"/> is not affected.</summary>
    public void FailNextFind(Exception failure) => failNextFind = failure;

    /// <summary>
    /// The next <see cref="TryCommitAsync"/> loses a race, as a version that moved on unless <paramref name="how"/>
    /// says otherwise, and stores and sends nothing.
    /// </summary>
    public void LoseNextCommit(LostRace? how = null)
    {
        loseNextCommit = true;
        this.how = how ?? new VersionMovedOn();
    }

    public async Task<Result<Unit, LostRace>> TryCommitAsync(CancellationToken cancellationToken)
    {
        if (loseNextCommit)
        {
            loseNextCommit = false;
            Discard();
            return Failure(how);
        }

        await CommitAsync(cancellationToken);
        return Success(Unit.Value);
    }

    private Option<Pharmacy> Lookup(PharmacyId id) =>
        Stored.SingleOrDefault(pharmacy => pharmacy.Id == id) is { } pharmacy ? Option<Pharmacy>.Some(pharmacy) : Option<Pharmacy>.None;

    private void Discard()
    {
        added.Clear();
        addedRecipients.Clear();
        addedSenders.Clear();
        queued.Clear();
    }

    private static PagedResult<Membership> PageOf(List<Membership> all, Page page, PageSize pageSize)
    {
        List<Membership> items = [.. all.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
        return new PagedResult<Membership>(items, all.Count, page.Value, pageSize.Value);
    }

    private IEnumerable<Membership> MembershipsOf(PharmacyId pharmacy, MembershipStatus status) =>
        Stored.Where(stored => stored.Id == pharmacy).SelectMany(stored => stored.Memberships).Where(membership => membership.Status == status);
}
