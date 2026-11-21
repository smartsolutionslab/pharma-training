namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

public class BlobNameTests
{
    [Fact]
    public void A_name_with_folders_up_to_1024_characters_is_accepted()
    {
        var longest = "courses/" + new string('a', 1024 - "courses/".Length);

        BlobName.From("courses/2026/handout.pdf").Value.ShouldBe("courses/2026/handout.pdf");
        BlobName.From(longest).Value.ShouldBe(longest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1025")]
    [InlineData("/a")]
    [InlineData("https://x/y")]
    [InlineData("a//b")]
    [InlineData("./a")]
    [InlineData("a/../b")]
    [InlineData("a/.")]
    public void An_unsafe_name_is_refused(string name)
    {
        var value = name == "1025" ? new string('a', 1025) : name;

        Should.Throw<GuardException>(() => BlobName.From(value));
    }
}
