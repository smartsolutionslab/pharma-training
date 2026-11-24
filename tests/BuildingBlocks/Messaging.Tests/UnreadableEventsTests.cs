using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// The events a pass of an announcement sweep could not read (#360): the pass logs one Warning for all of them, naming
/// how many, the first and the last by stream and version, and the first failure's type alone, never the exception: its
/// message may quote the stored data, which is about a learner.
/// </summary>
public class UnreadableEventsTests
{
    private const string EventType = "ContentCompleted";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeLogger logger;

    public UnreadableEventsTests()
    {
        logger = new FakeLogger(FakeLogCollector.Create(new FakeLogCollectorOptions { TimeProvider = time }));
    }

    [Fact]
    public void A_pass_that_skipped_events_logs_one_warning_with_their_count_and_the_first_and_last_stream_and_version()
    {
        var first = Found(streamVersion: 2, sequence: 1);
        var middle = Found(streamVersion: 5, sequence: 2);
        var last = Found(streamVersion: 9, sequence: 3);
        var unreadable = new UnreadableEvents(EventType);
        unreadable.Add(first, new InvalidCastException("The first stored event is of another type."));
        unreadable.Add(middle, new JsonException("The middle stored event does not match."));
        unreadable.Add(last, new JsonException("The last stored event does not match."));
        time.Advance(TimeSpan.FromSeconds(4));

        unreadable.LogSummary(logger);

        var warning = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Timestamp.ShouldBe(time.GetUtcNow());
        warning.Exception.ShouldBeNull();
        warning.GetStructuredStateValue("FirstFailureType").ShouldBe(nameof(InvalidCastException));
        warning.Message.ShouldNotContain("The first stored event", Case.Insensitive);
        warning.GetStructuredStateValue("EventType").ShouldBe(EventType);
        warning.GetStructuredStateValue("Count").ShouldBe("3");
        warning.GetStructuredStateValue("FirstStreamId").ShouldBe(first.StreamId.ToString());
        warning.GetStructuredStateValue("FirstStreamVersion").ShouldBe("2");
        warning.GetStructuredStateValue("LastStreamId").ShouldBe(last.StreamId.ToString());
        warning.GetStructuredStateValue("LastStreamVersion").ShouldBe("9");
        warning.Message.ShouldContain(first.StreamId.ToString(), Case.Insensitive);
        warning.Message.ShouldContain(last.StreamId.ToString(), Case.Insensitive);
        warning.Message.ShouldNotContain(middle.StreamId.ToString(), Case.Insensitive);
    }

    [Fact]
    public void A_pass_that_skipped_one_event_names_it_as_both_first_and_last()
    {
        var only = Found(streamVersion: 4, sequence: 7);
        var failure = new JsonException("The stored event does not match.");
        var unreadable = new UnreadableEvents(EventType);
        unreadable.Add(only, failure);

        unreadable.LogSummary(logger);

        var warning = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        warning.Exception.ShouldBeNull();
        warning.GetStructuredStateValue("FirstFailureType").ShouldBe(nameof(JsonException));
        warning.GetStructuredStateValue("Count").ShouldBe("1");
        warning.GetStructuredStateValue("FirstStreamId").ShouldBe(only.StreamId.ToString());
        warning.GetStructuredStateValue("LastStreamId").ShouldBe(only.StreamId.ToString());
        warning.GetStructuredStateValue("FirstStreamVersion").ShouldBe("4");
        warning.GetStructuredStateValue("LastStreamVersion").ShouldBe("4");
    }

    [Fact]
    public void A_pass_that_skipped_nothing_logs_nothing()
    {
        var unreadable = new UnreadableEvents(EventType);

        unreadable.LogSummary(logger);

        unreadable.Count.ShouldBe(0);
        logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void A_pass_at_its_most_events_still_logs_one_warning()
    {
        var unreadable = new UnreadableEvents(EventType);
        foreach (var sequence in Enumerable.Range(1, AnnouncementSweepSettings.MostEventsPerPass))
        {
            unreadable.Add(Found(streamVersion: sequence, sequence: sequence), new JsonException("The stored event does not match."));
        }

        unreadable.LogSummary(logger);

        var warning = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        warning.GetStructuredStateValue("Count").ShouldBe(AnnouncementSweepSettings.MostEventsPerPass.ToString(CultureInfo.InvariantCulture));
    }

    private UnannouncedEvent Found(long streamVersion, long sequence) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), streamVersion, time.GetUtcNow().AddHours(-sequence), sequence);
}
