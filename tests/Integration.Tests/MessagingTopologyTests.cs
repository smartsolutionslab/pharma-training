using Aspire.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What the AppHost derives for each module from its Application and the contracts, without a list of modules (#183): the
/// exchanges of the events its consumers take (C) and of the integration events of its own contract namespace (P), each
/// named after the event's full CLR name as Wolverine names the exchange. An event a module both publishes and consumes is
/// local to its host and never on the broker (docs/adr/0004), so it is in neither.
/// </summary>
public class MessagingTopologyTests
{
    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.";

    private const string ChapterFilesReleased = Contracts + "Editorial.ChapterFilesReleasedV1";

    private const string CourseVersionPublished = Contracts + "Editorial.CourseVersionPublishedV1";

    private const string CourseVersionRetired = Contracts + "Editorial.CourseVersionRetiredV1";

    private const string TestDefinitionPublished = Contracts + "Editorial.TestDefinitionPublishedV1";

    private const string InvitationMailRequested = Contracts + "Organisation.InvitationMailRequestedV1";

    private const string ContentCompleted = Contracts + "Learning.ContentCompletedV1";

    private const string LearningSessionStarted = Contracts + "Learning.LearningSessionStartedV1";

    [Fact]
    public void Catalog_consumes_the_two_course_version_exchanges_and_publishes_none()
    {
        var (consumed, published) = MessagingTopology.Of("catalog");

        consumed.ShouldBe([CourseVersionPublished, CourseVersionRetired], ignoreOrder: false);
        published.ShouldBeEmpty();
    }

    [Fact]
    public void Organisation_publishes_its_membership_and_pharmacy_events_and_leaves_its_local_mail_request_off_the_broker()
    {
        var (consumed, published) = MessagingTopology.Of("organisation");

        consumed.ShouldNotContain(InvitationMailRequested);
        consumed.ShouldBeEmpty();
        published.ShouldNotContain(InvitationMailRequested);
        published.ShouldBe(
            [Contracts + "Organisation.MembershipChangedV1", Contracts + "Organisation.MembershipEndedV1", Contracts + "Organisation.MembershipStartedV1", Contracts + "Organisation.PharmacyRegisteredV1"],
            ignoreOrder: false);
    }

    [Fact]
    public void Editorial_publishes_the_test_definition_and_the_released_files_beside_the_course_versions()
    {
        var (consumed, published) = MessagingTopology.Of("editorial");

        consumed.ShouldBeEmpty();
        published.ShouldBe([ChapterFilesReleased, CourseVersionPublished, CourseVersionRetired, TestDefinitionPublished], ignoreOrder: false);
    }

    [Fact]
    public void Compliance_consumes_the_course_versions_the_memberships_the_learning_progress_and_the_certificates()
    {
        var (consumed, _) = MessagingTopology.Of("compliance");

        consumed.ShouldBe(
            [Contracts + "Assessment.AttemptsExhaustedV1", Contracts + "Certification.CertificateIssuedV1", CourseVersionPublished, CourseVersionRetired, Contracts + "Learning.ContentCompletedV1", Contracts + "Learning.LearningSessionStartedV1", Contracts + "Organisation.MembershipChangedV1", Contracts + "Organisation.MembershipEndedV1", Contracts + "Organisation.MembershipStartedV1"],
            ignoreOrder: false);
    }

    /// <summary>
    /// #41: Learning copies the files Editorial releases, so its queue is bound to their exchange too; #194: it keeps a
    /// copy of the memberships, so it is bound to Organisation's three membership exchanges; #195: it keeps the
    /// retirements, so it is bound to the retirement's exchange.
    /// </summary>
    [Fact]
    public void Learning_consumes_the_course_versions_the_released_files_and_the_memberships_and_publishes_its_session_events()
    {
        var (consumed, published) = MessagingTopology.Of("learning");

        consumed.ShouldBe(
            [ChapterFilesReleased, CourseVersionPublished, CourseVersionRetired, Contracts + "Organisation.MembershipChangedV1", Contracts + "Organisation.MembershipEndedV1", Contracts + "Organisation.MembershipStartedV1"],
            ignoreOrder: false);
        published.ShouldBe([Contracts + "Learning.ContentCompletedV1", Contracts + "Learning.LearningSessionStartedV1"], ignoreOrder: false);
    }

    [Fact]
    public void Learning_consumes_the_two_course_version_exchanges_and_publishes_its_session_events()
    {
        var (consumed, published) = MessagingTopology.Of("learning");

        consumed.ShouldContain(CourseVersionPublished);
        consumed.ShouldContain(CourseVersionRetired);
        published.ShouldBe([ContentCompleted, LearningSessionStarted], ignoreOrder: false);
    }

    [Fact]
    public void The_module_is_matched_ignoring_case()
    {
        MessagingTopology.Of("Catalog").Consumed.ShouldBe([CourseVersionPublished, CourseVersionRetired], ignoreOrder: false);
    }

    [Fact]
    public void A_module_without_an_application_fails_naming_it_and_how_the_apphost_finds_one()
    {
        var failure = Should.Throw<InvalidOperationException>(() => MessagingTopology.Of("nowhere"));

        failure.Message.ShouldContain("nowhere");
        failure.Message.ShouldContain("*.Application");
    }

    [Fact]
    public void A_consumer_that_takes_a_service_of_another_assembly_beside_its_event_is_read_by_its_event_alone()
    {
        // AGENTS.md lets a consumer take dependencies as extra Handle parameters (IDispatcher, ILogger<T>,
        // Wolverine.IMessageContext); their assemblies are not among those the AppHost reads.
        using var probe = ConsumerProbe.Compile("Probe", """
            using Probe.Dependency;
            using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

            namespace SmartSolutionsLab.PharmaTraining.Modules.Probe.Application;

            public sealed class CourseVersionPublishedConsumer
            {
                public void Handle(CourseVersionPublishedV1 message, IMessageContext context)
                {
                }
            }
            """);

        var (consumed, published) = MessagingTopology.Of("probe", [probe.Application, probe.Contracts]);

        consumed.ShouldBe([CourseVersionPublished], ignoreOrder: false);
        published.ShouldBeEmpty();
    }

    [Fact]
    public void A_handler_method_whose_message_is_no_contract_grants_no_exchange()
    {
        // A helper named like a handler, or a typo in one, must not widen what the service's broker user may read.
        using var probe = ConsumerProbe.Compile("Probe", """
            using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

            namespace SmartSolutionsLab.PharmaTraining.Modules.Probe.Application;

            public sealed class CourseVersionPublishedConsumer
            {
                public void Handle(CourseVersionPublishedV1 message)
                {
                }

                public static void Handle(string note)
                {
                }
            }
            """);

        var (consumed, _) = MessagingTopology.Of("probe", [probe.Application, probe.Contracts]);

        consumed.ShouldBe([CourseVersionPublished], ignoreOrder: false);
    }

    [Fact]
    public async Task Every_service_host_publishes_only_its_own_modules_events_and_consumes_none_of_them()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var services = PublishedAppHost.Services(builder);

        services.ShouldNotBeEmpty();
        List<string> everyPublished = [];
        List<string> everyConsumed = [];
        foreach (var service in services)
        {
            var (consumed, published) = MessagingTopology.Of(service.Name);
            everyPublished.AddRange(published);
            everyConsumed.AddRange(consumed);
            var own = $"{Contracts}{service.Name}.";

            published.ShouldAllBe(exchange => exchange.StartsWith(own, StringComparison.OrdinalIgnoreCase), $"{service.Name} would be granted another module's exchanges.");
            consumed.ShouldAllBe(exchange => !exchange.StartsWith(own, StringComparison.OrdinalIgnoreCase), $"{service.Name} consumes an event of its own module from the broker; it is local.");
            consumed.ShouldBe([.. consumed.Order(StringComparer.Ordinal)], ignoreOrder: false, $"{service.Name}'s consumed exchanges are not sorted ordinally.");
            published.ShouldBe([.. published.Order(StringComparer.Ordinal)], ignoreOrder: false, $"{service.Name}'s published exchanges are not sorted ordinally.");
        }

        everyPublished.ShouldNotBeEmpty("no service publishes anything on the broker");
        everyPublished.ShouldBeUnique("two services would be granted the same event's exchange");
        everyConsumed.ShouldAllBe(exchange => everyPublished.Contains(exchange), "a service consumes an event no service publishes");
    }
}
