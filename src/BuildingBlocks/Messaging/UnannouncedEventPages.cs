using System.Runtime.CompilerServices;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The unannounced events one pass of an announcement sweep reads (#277, #337, docs/adr/0016), oldest first, a page of
/// <c>pageSize</c> at a time: each page is asked for after the <see cref="UnannouncedEvent.Sequence"/> of the last event
/// of the page before, so the pass reads on past events it skipped (they keep no marker and would otherwise come back on
/// every page). A page is asked for only once the caller has taken every event of the one before, so a caller that stops
/// once it has announced a batch reads no further. It ends at a page shorter than it asked for, the end of the window,
/// after <see cref="AnnouncementSweepSettings.MostPagesPerPass"/> pages, or once it has read
/// <see cref="AnnouncementSweepSettings.MostEventsPerPass"/> events (#351), the last page asking only for what is left of
/// them, so a pass never reads the whole window and, whatever its page size, loads at most that many events.
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
        var left = AnnouncementSweepSettings.MostEventsPerPass;
        for (var page = 0; page < AnnouncementSweepSettings.MostPagesPerPass && left > 0; page++)
        {
            var limit = Math.Min(pageSize, left);
            var found = await find(after, limit, cancellationToken);
            foreach (var stored in found)
            {
                yield return stored;
            }

            if (found.Count < limit)
            {
                yield break;
            }

            left -= found.Count;
            after = found[^1].Sequence;
        }
    }
}
