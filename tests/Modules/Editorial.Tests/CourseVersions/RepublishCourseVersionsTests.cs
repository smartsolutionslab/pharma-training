using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.DraftNextVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.PublishCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RetireCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An editor republishes what Editorial announced about its course versions (#189), for a subscriber that bound its
/// queue after the events went out: every version that was published gets its <see cref="CourseVersionPublishedV1"/>
/// and its <see cref="TestDefinitionPublishedV1"/> again, and every version that was retired its
/// <see cref="CourseVersionRetiredV1"/>, by course and version number, one commit per version. The versions reach
/// their states through the module's own handlers on the same store, so the events first announced are in
/// <see cref="InMemoryCourseVersionStore.Published"/> before the republish runs.
/// </summary>
public class RepublishCourseVersionsTests
{
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 7, 30, 0, TimeSpan.Zero);

    // The in-memory unit of work fills no metadata, and reading it throws: the payload is compared without it.
    private static readonly JsonSerializerOptions WithoutMetadata = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { DropMetadata } },
    };

    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeTimeProvider time = new(Start);

    [Fact]
    public async Task Every_published_version_is_announced_again_with_its_test_definition()
    {
        var hygiene = await PublishedAsync("Hygiene 2027");
        var storage = await PublishedAsync("Lagerung 2027");
        var before = store.Published.Count;

        var republished = await RepublishAsync();

        republished.IsSuccess.ShouldBeTrue();
        var events = store.Published.Skip(before).ToList();
        events.OfType<CourseVersionPublishedV1>().Select(announced => announced.CourseVersionId).ShouldBe([hygiene.Id.Value, storage.Id.Value], ignoreOrder: true);
        events.OfType<TestDefinitionPublishedV1>().Select(announced => announced.CourseVersionId).ShouldBe([hygiene.Id.Value, storage.Id.Value], ignoreOrder: true);
        events.OfType<CourseVersionRetiredV1>().ShouldBeEmpty();
        events.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_superseded_version_is_announced_again_too()
    {
        var first = await PublishedAsync("Hygiene 2027");
        var next = await PublishedNextAsync(first);
        first.Status.ShouldBe(CourseVersionStatus.Superseded);
        var before = store.Published.Count;

        await RepublishAsync();

        var events = store.Published.Skip(before).ToList();
        events.OfType<CourseVersionPublishedV1>().Select(announced => announced.CourseVersionId).ShouldBe([first.Id.Value, next.Id.Value], ignoreOrder: true);
        events.OfType<TestDefinitionPublishedV1>().Select(announced => announced.CourseVersionId).ShouldBe([first.Id.Value, next.Id.Value], ignoreOrder: true);
    }

    [Fact]
    public async Task A_retired_version_is_announced_as_published_then_as_retired()
    {
        var version = await PublishedAsync("Hygiene 2027");
        await RetiredAsync(version);
        var before = store.Published.Count;

        await RepublishAsync();

        var events = store.Published.Skip(before).ToList();
        events.Select(Describe).ShouldBe(
        [
            (nameof(CourseVersionPublishedV1), version.Id.Value),
            (nameof(TestDefinitionPublishedV1), version.Id.Value),
            (nameof(CourseVersionRetiredV1), version.Id.Value),
        ]);
    }

    /// <summary>Only what was announced goes out again: a draft and a version in review were never published.</summary>
    [Fact]
    public async Task Drafts_and_versions_in_review_are_not_announced()
    {
        store.Stored.Add(Draft("Entwurf 2027"));
        store.Stored.Add(InReview("In Prüfung 2027"));
        var published = await PublishedAsync("Hygiene 2027");
        var before = store.Published.Count;

        await RepublishAsync();

        store.Published.Skip(before).Select(announced => Describe(announced).Version).Distinct().ShouldBe([published.Id.Value]);
    }

    /// <summary>
    /// The versions are kept in the store in the reverse of the expected order, so the order comes from the handler and
    /// not from where they happen to be stored.
    /// </summary>
    [Fact]
    public async Task Versions_go_out_in_course_and_version_order_each_published_then_its_test_definition_then_its_retirement()
    {
        var first = await PublishedAsync("Hygiene 2027");
        var next = await PublishedNextAsync(first);
        await RetiredAsync(first);
        var other = await PublishedAsync("Lagerung 2027");
        var retired = await PublishedAsync("Betäubungsmittel 2027");
        await RetiredAsync(retired);
        store.Stored.Sort((left, right) => CompareByCourseAndNumber(right, left));
        List<CourseVersion> expectedOrder = [.. new[] { first, next, other, retired }.Order(Comparer<CourseVersion>.Create(CompareByCourseAndNumber))];
        List<(string Type, Guid Version)> expected = [];
        foreach (var version in expectedOrder)
        {
            expected.Add((nameof(CourseVersionPublishedV1), version.Id.Value));
            expected.Add((nameof(TestDefinitionPublishedV1), version.Id.Value));
            if (version.Status == CourseVersionStatus.Retired)
            {
                expected.Add((nameof(CourseVersionRetiredV1), version.Id.Value));
            }
        }

        var before = store.Published.Count;

        await RepublishAsync();

        store.Published.Skip(before).Select(Describe).ShouldBe(expected);
    }

    /// <summary>
    /// Every version reached its state through a handler, so every event first announced is in the store: each event the
    /// republish sends has an original of the same type and version, equal field for field but for the metadata.
    /// </summary>
    [Fact]
    public async Task Each_republished_event_carries_what_its_publish_or_retirement_announced()
    {
        var first = await PublishedAsync("Hygiene 2027");
        await PublishedNextAsync(first);
        await RetiredAsync(first);
        await PublishedAsync("Lagerung 2027");
        List<IntegrationEvent> originals = [.. store.Published];

        await RepublishAsync();

        var republished = store.Published.Skip(originals.Count).ToList();
        republished.Count.ShouldBe(originals.Count);
        foreach (var announced in republished)
        {
            var original = originals.Where(candidate => Describe(candidate) == Describe(announced)).ShouldHaveSingleItem(Describe(announced).ToString());
            Payload(announced).ShouldBe(Payload(original));
        }
    }

    /// <summary>One commit per announced version, holding that version's events and no other's.</summary>
    [Fact]
    public async Task Each_version_is_committed_on_its_own()
    {
        var first = await PublishedAsync("Hygiene 2027");
        await PublishedNextAsync(first);
        await RetiredAsync(first);
        await PublishedAsync("Lagerung 2027");
        store.Stored.Add(Draft("Entwurf 2027"));
        var commitsBefore = store.Commits;

        await RepublishAsync();

        (store.Commits - commitsBefore).ShouldBe(3);
        var commits = store.PublishedPerCommit.Skip(commitsBefore).ToList();
        commits.Count.ShouldBe(3);
        commits.ShouldAllBe(events => events.Count > 0);
        foreach (var events in commits)
        {
            events.Select(announced => Describe(announced).Version).Distinct().ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task The_answer_counts_the_versions_test_definitions_and_retirements_announced()
    {
        var first = await PublishedAsync("Hygiene 2027");
        await PublishedNextAsync(first);
        await RetiredAsync(first);
        await PublishedAsync("Lagerung 2027");
        store.Stored.Add(InReview("In Prüfung 2027"));

        var republished = await RepublishAsync();

        republished.IsSuccess.ShouldBeTrue();
        republished.Value.CourseVersions.ShouldBe(3);
        republished.Value.TestDefinitions.ShouldBe(3);
        republished.Value.Retirements.ShouldBe(1);
    }

    [Fact]
    public async Task With_nothing_ever_published_nothing_is_announced_or_committed_and_every_count_is_zero()
    {
        store.Stored.Add(Draft("Entwurf 2027"));
        store.Stored.Add(InReview("In Prüfung 2027"));

        var republished = await RepublishAsync();

        republished.IsSuccess.ShouldBeTrue();
        republished.Value.CourseVersions.ShouldBe(0);
        republished.Value.TestDefinitions.ShouldBe(0);
        republished.Value.Retirements.ShouldBe(0);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private static int CompareByCourseAndNumber(CourseVersion left, CourseVersion right)
    {
        var byCourse = left.Course.Value.CompareTo(right.Course.Value);
        if (byCourse != 0)
        {
            return byCourse;
        }

        return left.Number.Value.CompareTo(right.Number.Value);
    }

    private static (string Type, Guid Version) Describe(IntegrationEvent announced) =>
        announced switch
        {
            CourseVersionPublishedV1 published => (nameof(CourseVersionPublishedV1), published.CourseVersionId),
            TestDefinitionPublishedV1 test => (nameof(TestDefinitionPublishedV1), test.CourseVersionId),
            CourseVersionRetiredV1 retired => (nameof(CourseVersionRetiredV1), retired.CourseVersionId),
            _ => throw new InvalidOperationException($"Editorial announced an unexpected {announced.GetType().Name}."),
        };

    private static string Payload(IntegrationEvent announced) => JsonSerializer.Serialize(announced, announced.GetType(), WithoutMetadata);

    private static void DropMetadata(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var index = type.Properties.Count - 1; index >= 0; index--)
        {
            if (type.Properties[index].Name == nameof(IntegrationEvent.Metadata))
            {
                type.Properties.RemoveAt(index);
            }
        }
    }

    private Task<Result<EditorialRepublishedEventsDto, ApiError>> RepublishAsync() =>
        new RepublishCourseVersionsHandler(store, store).HandleAsync(new RepublishCourseVersions(), TestContext.Current.CancellationToken);

    /// <summary>A new course's first version, published through the handler by the reviewer.</summary>
    private async Task<CourseVersion> PublishedAsync(string title)
    {
        var version = InReview(title);
        store.Stored.Add(version);
        await PublishAsync(version);
        return version;
    }

    /// <summary>The next version of a published one, drafted, submitted and published through the handlers; it supersedes the other.</summary>
    private async Task<CourseVersion> PublishedNextAsync(CourseVersion published)
    {
        var drafted = await new DraftNextVersionHandler(store, store).HandleAsync(new DraftNextVersion(published.Id, Author), TestContext.Current.CancellationToken);
        drafted.IsSuccess.ShouldBeTrue();
        var next = store.Stored.Single(version => version.Id.Value == drafted.Value);
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        await PublishAsync(next);
        return next;
    }

    private async Task PublishAsync(CourseVersion version)
    {
        var published = await new PublishCourseVersionHandler(store, store, new FakeBlobUrls(time), ReviewSettings.Default, time)
            .HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);
        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
    }

    private async Task RetiredAsync(CourseVersion version)
    {
        var retired = await new RetireCourseVersionHandler(store, store, time).HandleAsync(new RetireCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);
        retired.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Retired);
    }
}
