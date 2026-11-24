namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// The pages one pass of an announcement sweep reads (#337), and their ceiling (#351): a pass reads at most
/// <see cref="AnnouncementSweepSettings.MostEventsPerPass"/> events, whatever its page size, asking the last page only for
/// what is left of it.
/// </summary>
public class UnannouncedEventPagesTests
{
    private static readonly DateTimeOffset StoredAt = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly List<int> asked = [];

    [Fact]
    public async Task A_pass_at_the_largest_page_reads_at_most_its_most_events()
    {
        var read = await ReadAsync(stored: AnnouncementSweepSettings.MostEventsPerPass * 3, pageSize: AnnouncementSweepSettings.MostBatchSize);

        read.ShouldBe(AnnouncementSweepSettings.MostEventsPerPass);
        asked.Sum().ShouldBe(AnnouncementSweepSettings.MostEventsPerPass);
    }

    [Fact]
    public async Task The_last_page_of_a_pass_asks_only_for_what_is_left_of_its_most_events()
    {
        var read = await ReadAsync(stored: AnnouncementSweepSettings.MostEventsPerPass * 3, pageSize: 300);

        read.ShouldBe(AnnouncementSweepSettings.MostEventsPerPass);
        asked.ShouldBe([300, 300, 300, AnnouncementSweepSettings.MostEventsPerPass - 900]);
    }

    [Fact]
    public async Task A_pass_at_the_default_batch_still_reads_its_most_pages()
    {
        var read = await ReadAsync(stored: AnnouncementSweepSettings.MostEventsPerPass * 3, pageSize: AnnouncementSweepSettings.DefaultBatchSize);

        read.ShouldBe(AnnouncementSweepSettings.MostPagesPerPass * AnnouncementSweepSettings.DefaultBatchSize);
        asked.Count.ShouldBe(AnnouncementSweepSettings.MostPagesPerPass);
    }

    [Fact]
    public async Task A_pass_ends_at_a_page_shorter_than_it_asked_for()
    {
        var read = await ReadAsync(stored: 250, pageSize: 100);

        read.ShouldBe(250);
        asked.ShouldBe([100, 100, 100]);
    }

    private async Task<int> ReadAsync(int stored, int pageSize)
    {
        List<UnannouncedEvent> events = [.. Enumerable.Range(1, stored).Select(sequence => new UnannouncedEvent(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, StoredAt, sequence))];
        var read = 0;
        await foreach (var _ in UnannouncedEventPages.ReadAsync(FindAsync, pageSize, TestContext.Current.CancellationToken))
        {
            read++;
        }

        return read;

        Task<IReadOnlyList<UnannouncedEvent>> FindAsync(long after, int limit, CancellationToken cancellationToken)
        {
            asked.Add(limit);
            IReadOnlyList<UnannouncedEvent> found = [.. events.Where(@event => @event.Sequence > after).Take(limit)];
            return Task.FromResult(found);
        }
    }
}
