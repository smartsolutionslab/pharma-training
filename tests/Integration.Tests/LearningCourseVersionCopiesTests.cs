using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Learning's local copy of Editorial's published course versions through the running application: the announcement
/// reaches Learning through the broker and is stored in its own tables with its chapters, once, however often it arrives.
/// </summary>
public class LearningCourseVersionCopiesTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_published_version_is_stored_with_its_course_number_and_chapters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7());

        await DeliverAsync(aspire, published, cancellationToken);

        (await VersionsAsync(published.CourseVersionId, cancellationToken)).ShouldHaveSingleItem()
            .ShouldBe(new VersionRow(published.CourseId, published.VersionNumber));
        (await ChaptersAsync(published.CourseVersionId, cancellationToken)).ShouldBe(
            [.. published.Chapters
                .OrderBy(chapter => chapter.Position)
                .Select(chapter => new ChapterRow(chapter.ChapterId, chapter.Position, chapter.Kind, chapter.VideoDurationSeconds))]);
    }

    [Fact]
    public async Task A_version_delivered_twice_is_stored_once_as_it_first_arrived()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7());
        await DeliverAsync(aspire, published, cancellationToken);

        // The same version again, under another envelope id and with other chapters, so the inbox lets it through to the consumer.
        await DeliverAsync(aspire, published with { VersionNumber = 2, Chapters = [published.Chapters[1] with { ChapterId = Guid.CreateVersion7() }] }, cancellationToken);

        (await VersionsAsync(published.CourseVersionId, cancellationToken)).ShouldHaveSingleItem().VersionNumber.ShouldBe(published.VersionNumber);
        (await ChaptersAsync(published.CourseVersionId, cancellationToken)).Select(chapter => chapter.ChapterId)
            .ShouldBe(published.Chapters.Select(chapter => chapter.ChapterId), ignoreOrder: true);
    }

    private async Task<List<VersionRow>> VersionsAsync(Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select course_id, version_number from learning.course_versions where id = @id", connection);
        command.Parameters.AddWithValue("id", courseVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<VersionRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new VersionRow(reader.GetGuid(0), reader.GetInt32(1)));
        }

        return rows;
    }

    private async Task<List<ChapterRow>> ChaptersAsync(Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select chapter_id, position, kind, video_duration_seconds from learning.course_version_chapters where course_version_id = @id order by position",
            connection);
        command.Parameters.AddWithValue("id", courseVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<ChapterRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ChapterRow(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }

        return rows;
    }

    private sealed record VersionRow(Guid CourseId, int VersionNumber);

    private sealed record ChapterRow(Guid ChapterId, int Position, string Kind, int? VideoDurationSeconds);
}
