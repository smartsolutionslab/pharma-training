using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Contracts.Certification;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>
/// Issuing: a passed test becomes exactly one certificate for its attempt, built from Certification's copies of the
/// person's membership and the course version, and announced as <see cref="CertificateIssuedV1"/> in the same commit.
/// </summary>
public class TestPassedV1ConsumerTests
{
    private readonly InMemoryCertificateStore certificates = new();
    private readonly InMemoryMembershipStore memberships = new();
    private readonly InMemoryCourseVersionStore courseVersions = new();
    private readonly Guid personId = Guid.CreateVersion7();
    private readonly Guid courseId = Guid.CreateVersion7();
    private readonly Guid courseVersionId = Guid.CreateVersion7();

    [Fact]
    public async Task A_passed_test_issues_a_certificate_with_the_stored_name_title_and_validity_from_the_day_of_passing()
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 24);
        var passed = TestPassedEvents.Passed(Guid.CreateVersion7(), personId, courseId, courseVersionId, 85);

        await HandleAsync(passed);

        var certificate = certificates.Stored.ShouldHaveSingleItem();
        certificate.Attempt.ShouldBe(AttemptId.From(passed.AttemptId));
        certificate.Holder.ShouldBe(PersonId.From(personId));
        certificate.HolderName.ShouldBe(HolderName.From("Anna Berger"));
        certificate.Course.ShouldBe(CourseId.From(courseId));
        certificate.CourseVersion.ShouldBe(CourseVersionId.From(courseVersionId));
        certificate.CourseTitle.ShouldBe(CourseTitle.From("Hygiene 2027"));
        certificate.Score.ShouldBe(ScorePercent.From(85));
        certificate.IssuedAt.ShouldBe(Timestamp.From(TestPassedEvents.PassedAt));
        certificate.ValidUntil.ShouldBe(CalendarDate.From(new DateOnly(2029, 3, 2)));
        certificate.Document.ShouldBe(DocumentState.Pending);
        certificate.Id.Value.Version.ShouldBe(7);
        certificates.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Issuing_publishes_one_certificate_issued_event_with_the_certificates_fields_in_the_same_commit()
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);

        await HandleAsync(TestPassedEvents.Passed(personId, courseId, courseVersionId));

        var certificate = certificates.Stored.ShouldHaveSingleItem();
        var issued = certificates.Published.ShouldHaveSingleItem().ShouldBeOfType<CertificateIssuedV1>();
        issued.CertificateId.ShouldBe(certificate.Id.Value);
        issued.PersonId.ShouldBe(personId);
        issued.CourseId.ShouldBe(courseId);
        issued.CourseVersionId.ShouldBe(courseVersionId);
        issued.IssuedAt.ShouldBe(TestPassedEvents.PassedAt);
        issued.ValidUntil.ShouldBe(new DateOnly(2028, 3, 2));
        certificates.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_issues_one_certificate_and_one_event()
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);
        var passed = TestPassedEvents.Passed(personId, courseId, courseVersionId);

        await HandleAsync(passed);
        await HandleAsync(passed);

        certificates.Stored.ShouldHaveSingleItem();
        certificates.Published.ShouldHaveSingleItem();
        certificates.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_attempt_published_again_under_a_new_event_identifier_issues_one_certificate_and_one_event()
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);
        var passed = TestPassedEvents.Passed(personId, courseId, courseVersionId);
        var republished = TestPassedEvents.Republished(passed);
        republished.Metadata.EventIdentifier.ShouldNotBe(passed.Metadata.EventIdentifier);

        await HandleAsync(passed);
        await HandleAsync(republished);

        certificates.Stored.ShouldHaveSingleItem().Attempt.ShouldBe(AttemptId.From(passed.AttemptId));
        certificates.Published.ShouldHaveSingleItem();
        certificates.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Two_passed_attempts_of_one_person_issue_two_certificates()
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);

        await HandleAsync(TestPassedEvents.Passed(personId, courseId, courseVersionId));
        await HandleAsync(TestPassedEvents.Passed(personId, courseId, courseVersionId));

        certificates.Stored.Count.ShouldBe(2);
        certificates.Published.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_holder_name_is_the_one_of_the_latest_started_membership()
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt.AddDays(10));
        StoreMembership(personId, "Anna Schmidt", MembershipEvents.StartedAt);
        StoreMembership(Guid.CreateVersion7(), "Somebody Else", MembershipEvents.StartedAt.AddDays(20));
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);

        await HandleAsync(TestPassedEvents.Passed(personId, courseId, courseVersionId));

        certificates.Stored.ShouldHaveSingleItem().HolderName.ShouldBe(HolderName.From("Anna Berger"));
    }

    [Fact]
    public async Task An_ended_membership_still_names_the_holder()
    {
        var ended = StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt.AddDays(10));
        StoreMembership(personId, "Anna Schmidt", MembershipEvents.StartedAt);
        memberships.Endings.Add(MembershipEnding.Of(ended.Id, ended.Person, ended.Pharmacy, Timestamp.From(MembershipEvents.EndedAt.AddDays(10))));
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);

        await HandleAsync(TestPassedEvents.Passed(personId, courseId, courseVersionId));

        certificates.Stored.ShouldHaveSingleItem().HolderName.ShouldBe(HolderName.From("Anna Berger"));
    }

    [Fact]
    public async Task Of_two_memberships_started_at_once_the_one_with_the_higher_id_names_the_holder()
    {
        var lower = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var higher = Guid.Parse("01900000-0000-7000-8000-000000000002");
        StoreMembership(higher, personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreMembership(lower, personId, "Anna Schmidt", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);

        await HandleAsync(TestPassedEvents.Passed(personId, courseId, courseVersionId));

        certificates.Stored.ShouldHaveSingleItem().HolderName.ShouldBe(HolderName.From("Anna Berger"));
    }

    [Fact]
    public async Task A_passed_test_before_its_membership_arrived_throws_to_be_retried_and_saves_nothing()
    {
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);
        var passed = TestPassedEvents.Passed(personId, courseId, courseVersionId);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(passed));

        refused.Message.ShouldContain(personId.ToString());
        NothingSaved();
    }

    [Fact]
    public async Task A_passed_test_before_its_course_version_arrived_throws_to_be_retried_and_saves_nothing()
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        var passed = TestPassedEvents.Passed(personId, courseId, courseVersionId);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(passed));

        refused.Message.ShouldContain(courseVersionId.ToString());
        NothingSaved();
    }

    [Fact]
    public async Task A_course_version_of_another_course_is_refused_naming_both_courses_and_saves_nothing()
    {
        var storedCourseId = Guid.CreateVersion7();
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, storedCourseId, "Hygiene 2027", 12);
        var passed = TestPassedEvents.Passed(personId, courseId, courseVersionId);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(passed));

        refused.Message.ShouldContain(courseId.ToString());
        refused.Message.ShouldContain(storedCourseId.ToString());
        NothingSaved();
    }

    [Theory]
    [InlineData(nameof(TestPassedV1.AttemptId))]
    [InlineData(nameof(TestPassedV1.SessionId))]
    [InlineData(nameof(TestPassedV1.PersonId))]
    [InlineData(nameof(TestPassedV1.CourseId))]
    [InlineData(nameof(TestPassedV1.CourseVersionId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_saved(string field)
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);
        var passed = TestPassedEvents.Passed(personId, courseId, courseVersionId);
        passed = field switch
        {
            nameof(TestPassedV1.AttemptId) => passed with { AttemptId = Guid.Empty },
            nameof(TestPassedV1.SessionId) => passed with { SessionId = Guid.Empty },
            nameof(TestPassedV1.PersonId) => passed with { PersonId = Guid.Empty },
            nameof(TestPassedV1.CourseId) => passed with { CourseId = Guid.Empty },
            nameof(TestPassedV1.CourseVersionId) => passed with { CourseVersionId = Guid.Empty },
            _ => throw new InvalidOperationException($"No field {field}."),
        };

        var refused = await Should.ThrowAsync<GuardException>(() => HandleAsync(passed));

        refused.ParamName.ShouldBe(field);
        NothingSaved();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task A_score_outside_zero_to_one_hundred_percent_is_refused_by_its_name_and_nothing_saved(int score)
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);
        var passed = TestPassedEvents.Passed(Guid.CreateVersion7(), personId, courseId, courseVersionId, score);

        var refused = await Should.ThrowAsync<GuardException>(() => HandleAsync(passed));

        refused.ParamName.ShouldBe(nameof(TestPassedV1.ScorePercent));
        NothingSaved();
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_saved()
    {
        await Should.ThrowAsync<GuardException>(() => HandleAsync(null!));

        NothingSaved();
    }

    [Fact]
    public async Task Two_certificates_get_different_url_safe_verification_codes_of_32_characters()
    {
        StoreMembership(personId, "Anna Berger", MembershipEvents.StartedAt);
        StoreCourseVersion(courseVersionId, courseId, "Hygiene 2027", 12);

        await HandleAsync(TestPassedEvents.Passed(personId, courseId, courseVersionId));
        await HandleAsync(TestPassedEvents.Passed(personId, courseId, courseVersionId));

        var codes = certificates.Stored.Select(certificate => certificate.VerificationCode.Value).ToList();
        codes.Count.ShouldBe(2);
        codes.Distinct().Count().ShouldBe(2);
        codes.ShouldAllBe(code => code.Length == 32);
        codes.ShouldAllBe(code => code.All(character => char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_'));
        codes.ShouldAllBe(code => VerificationCode.From(code).Value == code);
    }

    private Task HandleAsync(TestPassedV1 message) =>
        new TestPassedV1Consumer(certificates, memberships, courseVersions, certificates).HandleAsync(message, TestContext.Current.CancellationToken);

    private Membership StoreMembership(Guid person, string name, DateTimeOffset startedAt) =>
        StoreMembership(Guid.CreateVersion7(), person, name, startedAt);

    private Membership StoreMembership(Guid membershipId, Guid person, string name, DateTimeOffset startedAt)
    {
        var membership = Membership.Started(
            MembershipId.From(membershipId),
            PersonId.From(person),
            PharmacyId.From(Guid.CreateVersion7()),
            HolderName.From(name),
            Timestamp.From(startedAt));
        memberships.Memberships.Add(membership);
        return membership;
    }

    private void StoreCourseVersion(Guid version, Guid course, string title, int validityMonths) =>
        courseVersions.Versions.Add(CourseVersion.Published(CourseVersionId.From(version), CourseId.From(course), CourseTitle.From(title), RecurrenceInterval.From(validityMonths)));

    private void NothingSaved()
    {
        certificates.Stored.ShouldBeEmpty();
        certificates.Published.ShouldBeEmpty();
        certificates.Commits.ShouldBe(0);
    }
}
