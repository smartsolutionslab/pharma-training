using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Assessment's local copy of Editorial's published test definitions through the running application: the definition
/// reaches Assessment through the broker and is stored in its own tables with its questions, options and correctness,
/// once, however often it arrives.
/// </summary>
public class AssessmentTestDefinitionCopiesTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_published_test_definition_is_stored_with_its_questions_and_options()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Definition(Guid.CreateVersion7());

        await DeliverAsync(aspire, DefinitionType, published, cancellationToken);

        (await DefinitionsAsync(published.CourseVersionId, cancellationToken)).ShouldHaveSingleItem()
            .ShouldBe(new DefinitionRow(published.CourseId, published.PassingScorePercent, published.MaxAttempts));
        (await QuestionsAsync(published.CourseVersionId, cancellationToken)).ShouldBe(
            [.. published.Questions.Select((question, index) => new QuestionRow(question.QuestionId, index + 1, question.ChapterId, question.Type, question.Text))]);
        (await OptionsAsync(published.CourseVersionId, cancellationToken)).ShouldBe(
            [.. published.Questions.SelectMany(question => question.Options.Select((option, index) =>
                new OptionRow(question.QuestionId, option.AnswerOptionId, index + 1, option.Text, option.IsCorrect)))]);
    }

    [Fact]
    public async Task A_test_definition_delivered_twice_is_stored_once_as_it_first_arrived()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Definition(Guid.CreateVersion7());
        await DeliverAsync(aspire, DefinitionType, published, cancellationToken);

        // The same version republished: a new event identifier, another envelope id and other content. The first copy wins.
        await DeliverAsync(
            aspire,
            DefinitionType,
            published with
            {
                PassingScorePercent = 50,
                MaxAttempts = 5,
                Questions = [published.Questions[1] with { QuestionId = Guid.CreateVersion7() }],
                Metadata = new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()),
            },
            cancellationToken);

        (await DefinitionsAsync(published.CourseVersionId, cancellationToken)).ShouldHaveSingleItem()
            .ShouldBe(new DefinitionRow(published.CourseId, published.PassingScorePercent, published.MaxAttempts));
        (await QuestionsAsync(published.CourseVersionId, cancellationToken)).Select(question => question.QuestionId)
            .ShouldBe(published.Questions.Select(question => question.QuestionId));
        (await OptionsAsync(published.CourseVersionId, cancellationToken)).Count
            .ShouldBe(published.Questions.Sum(question => question.Options.Count));
    }

    private async Task<List<DefinitionRow>> DefinitionsAsync(Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select course_id, passing_score_percent, max_attempts from assessment.test_definitions where course_version_id = @id",
            connection);
        command.Parameters.AddWithValue("id", courseVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<DefinitionRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DefinitionRow(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return rows;
    }

    private async Task<List<QuestionRow>> QuestionsAsync(Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select question_id, position, chapter_id, type, text from assessment.test_definition_questions where course_version_id = @id order by position",
            connection);
        command.Parameters.AddWithValue("id", courseVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<QuestionRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new QuestionRow(reader.GetGuid(0), reader.GetInt32(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4)));
        }

        return rows;
    }

    private async Task<List<OptionRow>> OptionsAsync(Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select o.question_id, o.option_id, o.position, o.text, o.is_correct
            from assessment.test_definition_options o
            join assessment.test_definition_questions q on q.course_version_id = o.course_version_id and q.question_id = o.question_id
            where o.course_version_id = @id
            order by q.position, o.position
            """,
            connection);
        command.Parameters.AddWithValue("id", courseVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<OptionRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OptionRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3), reader.GetBoolean(4)));
        }

        return rows;
    }

    private sealed record DefinitionRow(Guid CourseId, int PassingScorePercent, int MaxAttempts);

    private sealed record QuestionRow(Guid QuestionId, int Position, Guid ChapterId, string Type, string Text);

    private sealed record OptionRow(Guid QuestionId, Guid OptionId, int Position, string Text, bool IsCorrect);
}
