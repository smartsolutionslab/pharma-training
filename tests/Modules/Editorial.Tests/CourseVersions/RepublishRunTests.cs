using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// How a republish runs (ADR 0019): one run at a time, held by <see cref="IRepublishLock"/>, a second answered as
/// <c>editorial.republish-running</c> without sending anything; every version committed through
/// <see cref="IEditorialUnitOfWork.CommitOneOfSeveralAsync"/>, so each commit's events go out at once; and one log line
/// per run naming the editor and what was sent.
/// </summary>
public class RepublishRunTests
{
    private readonly InMemoryCourseVersionStore store = new();
    private readonly InMemoryRepublishLock runLock = new();
    private readonly FakeLogger<RepublishCourseVersionsHandler> logger = new();

    [Fact]
    public async Task A_republish_while_another_runs_is_refused_as_running_and_sends_nothing()
    {
        store.Stored.Add(Published("Hygiene 2027"));
        await using var other = runLock.HoldAsAnotherRun();

        var republished = await RepublishAsync(store);

        republished.IsFailure.ShouldBeTrue();
        republished.Error.Code.ShouldBe("editorial.republish-running");
        republished.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_run_takes_the_lock_and_releases_it_when_it_ends()
    {
        store.Stored.Add(Published("Hygiene 2027"));

        (await RepublishAsync(store)).IsSuccess.ShouldBeTrue();
        (await RepublishAsync(store)).IsSuccess.ShouldBeTrue();

        runLock.Taken.ShouldBe(2);
        runLock.Held.ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_whose_commit_fails_releases_the_lock()
    {
        store.Stored.Add(Published("Hygiene 2027"));
        var failing = Substitute.For<IEditorialUnitOfWork>();
        failing.CommitOneOfSeveralAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("The broker is gone.")));

        await Should.ThrowAsync<InvalidOperationException>(() => RepublishAsync(failing));

        runLock.Held.ShouldBeFalse();
    }

    [Fact]
    public async Task Every_version_is_committed_as_one_of_several()
    {
        store.Stored.Add(Published("Hygiene 2027"));
        store.Stored.Add(Published("Lagerung 2027"));

        await RepublishAsync(store);

        store.CommitsOfSeveral.ShouldBe(2);
        store.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task A_run_logs_the_editor_and_what_it_sent()
    {
        store.Stored.Add(Published("Hygiene 2027"));

        await RepublishAsync(store);

        var line = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        line.Level.ShouldBe(LogLevel.Information);
        line.Message.ShouldContain(Author.Value.ToString());
        line.Message.ShouldContain("CourseVersions = 1");
        line.Message.ShouldContain("TestDefinitions = 1");
        line.Message.ShouldContain("Retirements = 0");
    }

    [Fact]
    public async Task A_refused_run_logs_the_editor()
    {
        await using var other = runLock.HoldAsAnotherRun();

        await RepublishAsync(store);

        var line = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        line.Level.ShouldBe(LogLevel.Information);
        line.Message.ShouldContain(Author.Value.ToString());
        line.Message.ShouldContain("running");
    }

    private Task<Result<EditorialRepublishedEventsDto, ApiError>> RepublishAsync(IEditorialUnitOfWork unitOfWork) =>
        new RepublishCourseVersionsHandler(store, unitOfWork, new FakeBlobUrls(TimeProvider.System), runLock, logger)
            .HandleAsync(new RepublishCourseVersions(Author), TestContext.Current.CancellationToken);
}
