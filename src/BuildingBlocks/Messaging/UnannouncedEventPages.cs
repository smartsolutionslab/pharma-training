using System.Runtime.CompilerServices;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The unannounced events one pass of an announcement sweep reads (#277, #337, docs/adr/0016), oldest first, a page of
/// <c>pageSize</c> at a time: each page is asked for after the <see cref="UnannouncedEvent.Sequence"/> of the last event
/// of the page before, so the pass reads on past events it skipped (they keep no marker and would otherwise come back on
/// every page). A page is asked for only once the caller has taken every event of the one before, so a caller that stops
/// once it has announced a batch reads no further. It ends at a page shorter than <c>pageSize</c>, the end of the window,
/// or after <see cref="AnnouncementSweepSettings.MostPagesPerPass"/> pages, so a pass never reads the whole window.
/// </summary>
public static class UnannouncedEventPages
{
    /// <param name="find">Answers at most <c>limit</c> unannounced events of the window after the sequence <c>after</c>, oldest first.</param>
    public static async IAsyncEnumerable<UnannouncedEvent> ReadAsync(
        Func<long, int, CancellationToken, Task<IReadOnlyList<UnannouncedEvent>>> find,
        int pageSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Ensure.That(find).IsNotNull();
        Ensure.That(pageSize).AtLeast(1);

        var after = UnannouncedEvent.BeforeFirst;
        for (var page = 0; page < AnnouncementSweepSettings.MostPagesPerPass; page++)
        {
            var found = await find(after, pageSize, cancellationToken);
            foreach (var stored in found)
            {
                yield return stored;
            }

            if (found.Count < pageSize)
            {
                yield break;
            }

            after = found[^1].Sequence;
        }
    }
}
