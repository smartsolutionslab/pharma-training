using System.Text;
using System.Text.RegularExpressions;
using Aspire.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The regular expression a broker user's permission is written as (docs/adr/0011): exactly the given queue and exchange
/// names, anchored, with the dotted prefixes they share written once, so a user who consumes many events stays under the
/// length RabbitMQ accepts. RabbitMQ 4.3.7 refused a pattern over 1024 bytes and stopped at boot, and every consumed event
/// added its full type name, about 60 bytes.
/// </summary>
public class BrokerPermissionPatternTests
{
    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.";

    private static readonly string[] Names =
    [
        "compliance",
        Contracts + "Assessment.AttemptsExhaustedV1",
        Contracts + "Compliance.AssignmentCompletedV1",
        Contracts + "Compliance.CourseAssignedV1",
        Contracts + "Organisation.MembershipStartedV1",
    ];

    [Fact]
    public void The_pattern_matches_every_given_name()
    {
        var pattern = BrokerPermissionPattern.Of("compliance", Names);

        Names.ShouldAllBe(name => Regex.IsMatch(name, pattern));
    }

    [Theory]
    [InlineData("compliance2")]
    [InlineData("xcompliance")]
    [InlineData("learning")]
    [InlineData(Contracts + "Compliance.AssignmentCompletedV2")]
    [InlineData(Contracts + "Compliance.AssignmentCompletedV1.x")]
    [InlineData(Contracts + "Compliance")]
    [InlineData(Contracts + "Learning.ContentCompletedV1")]
    [InlineData("SmartSolutionsLabXPharmaTraining.Contracts.Compliance.CourseAssignedV1")]
    public void The_pattern_matches_no_other_name(string other)
    {
        var pattern = BrokerPermissionPattern.Of("compliance", Names);

        Regex.IsMatch(other, pattern).ShouldBeFalse($"{pattern} matches {other}.");
    }

    [Fact]
    public void The_pattern_writes_a_shared_prefix_once()
    {
        var pattern = BrokerPermissionPattern.Of("compliance", Names);

        Regex.Count(pattern, Regex.Escape(Regex.Escape(Contracts))).ShouldBe(1, pattern);
        Regex.Count(pattern, Regex.Escape(Regex.Escape("Compliance."))).ShouldBe(1, pattern);
    }

    [Fact]
    public void The_pattern_is_anchored_at_both_ends()
    {
        var pattern = BrokerPermissionPattern.Of("compliance", ["compliance"]);

        pattern.ShouldStartWith("^(");
        pattern.ShouldEndWith(")$");
    }

    [Fact]
    public void A_name_that_is_a_dotted_prefix_of_another_still_matches_alone()
    {
        var pattern = BrokerPermissionPattern.Of("owner", ["a.b", "a.b.c"]);

        Regex.IsMatch("a.b", pattern).ShouldBeTrue(pattern);
        Regex.IsMatch("a.b.c", pattern).ShouldBeTrue(pattern);
        Regex.IsMatch("a.b.d", pattern).ShouldBeFalse(pattern);
        Regex.IsMatch("a", pattern).ShouldBeFalse(pattern);
    }

    [Fact]
    public void A_character_that_means_something_in_a_regular_expression_is_escaped()
    {
        var pattern = BrokerPermissionPattern.Of("owner", ["a+b", "c(d)"]);

        Regex.IsMatch("a+b", pattern).ShouldBeTrue(pattern);
        Regex.IsMatch("aab", pattern).ShouldBeFalse(pattern);
        Regex.IsMatch("c(d)", pattern).ShouldBeTrue(pattern);
    }

    [Fact]
    public void Sixty_consumed_events_stay_under_the_length_RabbitMQ_accepts()
    {
        string[] events = [.. Enumerable.Range(0, 60).Select(index => $"{Contracts}Module{index % 7}.SomethingHappenedNumber{index}V1")];

        var pattern = BrokerPermissionPattern.Of("owner", ["owner", .. events]);

        Encoding.UTF8.GetByteCount(pattern).ShouldBeLessThanOrEqualTo(BrokerPermissionPattern.MaximumLength);
        events.ShouldAllBe(name => Regex.IsMatch(name, pattern));
    }

    [Fact]
    public void A_pattern_longer_than_RabbitMQ_accepts_stops_the_AppHost_naming_its_user()
    {
        string[] events = [.. Enumerable.Range(0, 200).Select(index => $"{Contracts}Module{index}.SomethingHappenedNumber{index}V1")];

        var stopped = Should.Throw<InvalidOperationException>(() => BrokerPermissionPattern.Of("compliance", events));

        stopped.Message.ShouldContain("compliance");
        stopped.Message.ShouldContain(BrokerPermissionPattern.MaximumLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
