using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// The edge under <c>/api</c>, with the module behaviour the seeder relies on, each rule as the real module keeps it where
/// it matters to the seed: Organisation (pharmacies, invitations mailed with a token, accepting), Editorial (drafts,
/// four eyes: approver, submitter and publisher are three people), the catalog, Compliance (members, assignments refused
/// when due before today in Germany), Learning, Assessment (shuffled options, three attempts) and Certification. Every
/// change it makes counts in <see cref="Changes"/>. A path it has no route for answers 501.
/// </summary>
public sealed class FakeEdge(FakeKeycloak identity, FakeMailpit mail, FakeTimeProvider time)
{
    public const int MaxAttempts = 3;

    private static readonly TimeZoneInfo Germany = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly List<Pharmacy> pharmacies = [];

    private readonly List<Membership> memberships = [];

    private readonly List<Invitation> invitations = [];

    private readonly List<CourseVersion> versions = [];

    private readonly List<Assignment> assignments = [];

    private readonly List<Session> sessions = [];

    private readonly List<Examination> examinations = [];

    private readonly List<Certificate> certificates = [];

    private readonly Dictionary<string, string> persons = [with(StringComparer.Ordinal)];

    /// <summary>
    /// The deployment <c>/api/config.json</c> reports in <c>environment</c>, as the AppHost hands the edge its environment's
    /// name; run mode's by default. Null leaves the field out, as an edge from before #457 PR 2 answers.
    /// </summary>
    public string? ReportedEnvironment { get; set; } = "Development";

    /// <summary>The realm <c>/api/config.json</c> reports in <c>oidcAuthority</c>: the fake Keycloak's, at its loopback origin, by default.</summary>
    public string ReportedAuthority { get; set; } = FakeKeycloak.RealmUrl;

    /// <summary>How many times the edge stored something.</summary>
    public int Changes { get; private set; }

    /// <summary>How many more assignments Compliance refuses with 404 compliance.course-not-found, its copy of a just published course still missing.</summary>
    public int CourseCopyMissing { get; set; }

    /// <summary>How many more session starts Learning refuses with 403 learning.membership-missing, its copy of a just accepted membership still missing.</summary>
    public int MembershipMissing { get; set; }

    /// <summary>Today in Germany by the fake clock: the day Compliance compares a due date with.</summary>
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Germany).DateTime);

    public IReadOnlyList<string> PharmacyNames => [.. pharmacies.Select(pharmacy => pharmacy.Name)];

    /// <summary>The members of a pharmacy by username, the manager included.</summary>
    public IReadOnlyList<string> MembersOf(string pharmacy) =>
        [.. memberships.Where(membership => pharmacies.Single(each => each.Name == pharmacy).Id == membership.PharmacyId).Select(membership => membership.User)];

    public IReadOnlyList<string> OpenInvitations => [.. invitations.Where(invitation => invitation.Open).Select(invitation => invitation.Email)];

    /// <summary>Every course version as title, number and status.</summary>
    public IReadOnlyList<(string Title, int Number, string Status)> Versions => [.. versions.Select(version => (version.Title, version.Number, version.Status))];

    /// <summary>A version's chapter titles and question texts, in order.</summary>
    public (IReadOnlyList<string> Chapters, IReadOnlyList<string> Questions) ContentOf(string title, int number)
    {
        var version = versions.Single(each => each.Title == title && each.Number == number);
        return ([.. version.Chapters.Select(chapter => chapter.Title)], [.. version.Questions.Select(question => question.Text)]);
    }

    /// <summary>Every assignment as learner, course title, due date and recurrence.</summary>
    public IReadOnlyList<(string User, string Course, DateOnly Due, int? RecurrenceMonths)> Assignments =>
        [.. assignments.Select(assignment => (UserOf(assignment.PersonId), TitleOf(assignment.CourseId), assignment.Due, assignment.RecurrenceMonths))];

    /// <summary>Every examination as learner, course title, version number, status and attempts used.</summary>
    public IReadOnlyList<(string User, string Course, int Version, string Status, int AttemptsUsed)> Examinations =>
        [.. examinations.Select(exam =>
        {
            var version = versions.Single(each => each.Id == exam.VersionId);
            return (exam.User, version.Title, version.Number, exam.Status, exam.AttemptsUsed);
        })];

    /// <summary>Every certificate as holder and course title.</summary>
    public IReadOnlyList<(string User, string Course)> Certificates => [.. certificates.Select(certificate => (certificate.User, certificate.Title))];

    /// <summary>The chapters a learner reported progress on, by course title, in order.</summary>
    public IReadOnlyList<string> CompletedChapters(string user, string course) =>
        [.. sessions.Where(session => session.User == user && TitleOf(session.CourseId) == course)
            .SelectMany(session => versions.Single(version => version.Id == session.VersionId).Chapters.Where(chapter => session.Completed.Contains(chapter.Id)))
            .Select(chapter => chapter.Title)];

    /// <summary>Renews every open invitation as a manager's renew does: a new token, mailed; the old one no longer accepted.</summary>
    public void RenewOpenInvitations()
    {
        foreach (var invitation in invitations.Where(each => each.Open))
        {
            invitation.Token = NewToken();
            Mail(invitation);
        }
    }

    /// <summary>Whether an option id an attempt showed is the option the editor marked correct.</summary>
    public bool IsCorrect(string attemptOptionId)
    {
        var editorOption = examinations.Single(exam => exam.OptionIds.ContainsKey(attemptOptionId)).OptionIds[attemptOptionId];
        return versions.SelectMany(version => version.Questions).SelectMany(question => question.Options).Single(option => option.Id == editorOption).Correct;
    }

    /// <summary>How many progress reports reached an exhausted examination's session.</summary>
    public int ProgressAfterExhaustion => examinations.Sum(exam => exam.ProgressAfterExhaustion);

    /// <summary>Whether a blueprint was set after a question of its version was approved (docs/adr/0007: the seed never does).</summary>
    public bool BlueprintChangedAfterApproval => versions.Any(version => version.BlueprintChangedAfterApproval);

    internal FakeAnswer Handle(HttpRequestMessage request, string body)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("A request without a URI.");
        if (!uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
        {
            return new FakeAnswer(string.Empty, null, FakeResponses.Unscripted(request));
        }

        var path = uri.AbsolutePath[4..];
        var query = FakeRoute.Pairs(uri.Query);
        var method = request.Method.Method;
        if (method == "GET" && path == "/config.json")
        {
            var configuration = new JsonObject { ["oidcAuthority"] = ReportedAuthority, ["clientId"] = FakeKeycloak.ClientId };
            if (ReportedEnvironment is not null)
            {
                configuration["environment"] = ReportedEnvironment;
            }

            return new FakeAnswer("/config.json", null, FakeResponses.Ok(configuration));
        }

        var user = identity.UserOf(request.Headers.Authorization?.ToString());
        foreach (var (routeMethod, template, handler) in Routes())
        {
            if (routeMethod != method || !FakeRoute.TryMatch(template, path, out var values))
            {
                continue;
            }

            if (user is null)
            {
                return new FakeAnswer(template, null, new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            var json = string.IsNullOrEmpty(body) ? new JsonObject() : JsonNode.Parse(body) ?? new JsonObject();
            return new FakeAnswer(template, user, handler(new Call(user, values, query, json)));
        }

        return new FakeAnswer(string.Empty, user, FakeResponses.Unscripted(request));
    }

    private static string NewToken() => Convert.ToBase64String(Guid.CreateVersion7().ToByteArray().Concat(Guid.CreateVersion7().ToByteArray()).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_')[..43];

    private static string Id() => Guid.CreateVersion7().ToString();

    private static (int Page, int PageSize)? Paging(Dictionary<string, string> query)
    {
        var page = int.Parse(query.GetValueOrDefault("page") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        var pageSize = int.Parse(query.GetValueOrDefault("pageSize") ?? "20", System.Globalization.CultureInfo.InvariantCulture);
        return page < 1 || pageSize < 1 || pageSize > 100 ? null : (page, pageSize);
    }

    private static HttpResponseMessage Paged<T>(Dictionary<string, string> query, IReadOnlyList<T> all)
    {
        if (Paging(query) is not { } paging)
        {
            return FakeResponses.Problem(HttpStatusCode.BadRequest, "invalid-argument");
        }

        var (page, pageSize) = paging;

        return FakeResponses.Ok(new { items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), totalCount = all.Count, page, pageSize });
    }

    private IEnumerable<(string Method, string Template, Func<Call, HttpResponseMessage> Handler)> Routes() =>
    [
        ("POST", "/organisation/pharmacies", RegisterPharmacy),
        ("GET", "/organisation/pharmacies/mine", MyPharmacies),
        ("POST", "/organisation/pharmacies/{id}/invitations", Invite),
        ("GET", "/organisation/pharmacies/{id}/invitations", ListInvitations),
        ("POST", "/organisation/invitations/accept", Accept),
        ("POST", "/editorial/courses", DraftCourse),
        ("GET", "/editorial/course-versions", ListVersions),
        ("GET", "/editorial/course-versions/{id}", GetVersion),
        ("POST", "/editorial/course-versions/{id}/chapters", AddChapter),
        ("PUT", "/editorial/course-versions/{id}/blueprint", SetBlueprint),
        ("POST", "/editorial/course-versions/{id}/questions", AddQuestion),
        ("POST", "/editorial/course-versions/{id}/questions/{questionId}/approve", Approve),
        ("POST", "/editorial/course-versions/{id}/submit", Submit),
        ("POST", "/editorial/course-versions/{id}/publish", Publish),
        ("POST", "/editorial/course-versions/{id}/next", NextVersion),
        ("GET", "/catalog/courses", Catalog),
        ("GET", "/compliance/pharmacies/{id}/members", ComplianceMembers),
        ("POST", "/compliance/pharmacies/{id}/assignments", Assign),
        ("GET", "/compliance/me/assignments", MyAssignments),
        ("GET", "/compliance/pharmacies/{id}/matrix", Matrix),
        ("POST", "/learning/sessions", StartSession),
        ("POST", "/learning/sessions/{id}/progress", Progress),
        ("GET", "/assessment/examinations/by-session/{sessionId}", ExaminationBySession),
        ("POST", "/assessment/examinations/{id}/attempts", StartAttempt),
        ("PUT", "/assessment/examinations/{id}/attempts/current/answers/{questionId}", Answer),
        ("POST", "/assessment/examinations/{id}/attempts/current/submit", SubmitAttempt),
        ("GET", "/certification/me/certificates", MyCertificates),
    ];

    private HttpResponseMessage Changed(HttpResponseMessage response)
    {
        Changes++;
        return response;
    }

    private string PersonOf(string user)
    {
        if (!persons.TryGetValue(user, out var person))
        {
            person = Id();
            persons[user] = person;
        }

        return person;
    }

    private string UserOf(string person) => persons.Single(each => each.Value == person).Key;

    private string TitleOf(string courseId) => versions.First(version => version.CourseId == courseId).Title;

    private bool Manages(string user, string pharmacyId) =>
        memberships.Any(membership => membership.PharmacyId == pharmacyId && membership.User == user && membership.Role == "Manager");

    private CourseVersion? Published(string courseId) =>
        versions.Where(version => version.CourseId == courseId && version.Status == "Published").MaxBy(version => version.Number);

    private void Mail(Invitation invitation)
    {
        var pharmacy = pharmacies.Single(each => each.Id == invitation.PharmacyId);
        mail.Deliver(
            invitation.Email,
            $"Einladung zu {pharmacy.Name} bei PharmaTraining",
            $"Sie wurden eingeladen.\n\nÖffnen Sie diesen Link, um die Einladung anzunehmen:\n{FakeApplication.WebOrigin}invitations/accept#token={invitation.Token}\n");
    }

    private HttpResponseMessage RegisterPharmacy(Call call)
    {
        var name = call.Body["name"]?.GetValue<string>() ?? string.Empty;
        var pharmacy = new Pharmacy(Id(), name);
        pharmacies.Add(pharmacy);
        memberships.Add(new Membership(Id(), pharmacy.Id, call.User, "Manager", call.Body["jobFunction"]?.GetValue<string>() ?? "Pharmacist"));
        return Changed(FakeResponses.Created(pharmacy.Id, $"/organisation/pharmacies/{pharmacy.Id}"));
    }

    private HttpResponseMessage MyPharmacies(Call call) => Paged(call.Query, [.. memberships.Where(membership => membership.User == call.User).Select(membership =>
    {
        var pharmacy = pharmacies.Single(each => each.Id == membership.PharmacyId);
        var manager = membership.Role == "Manager";
        return new { id = pharmacy.Id, name = pharmacy.Name, membershipId = membership.Id, role = membership.Role, jobFunction = membership.JobFunction, canManageMembers = manager, canAssignCourses = manager };
    })]);

    private HttpResponseMessage Invite(Call call)
    {
        var pharmacyId = call.Route["id"];
        if (!Manages(call.User, pharmacyId))
        {
            return FakeResponses.Problem(HttpStatusCode.Forbidden, "organisation.not-a-manager");
        }

        var email = call.Body["email"]?.GetValue<string>() ?? string.Empty;
        if (invitations.Any(invitation => invitation.Open && invitation.PharmacyId == pharmacyId && invitation.Email == email))
        {
            // The recipient cooldown (docs/adr/0004): an address is mailed at most every ten minutes.
            return FakeResponses.Problem(HttpStatusCode.TooManyRequests, "organisation.invitation-recipient-cooldown");
        }

        var invitation = new Invitation(Id(), pharmacyId, email, call.Body["jobFunction"]?.GetValue<string>() ?? "Other", NewToken());
        invitations.Add(invitation);
        Mail(invitation);
        return Changed(FakeResponses.Created(invitation.MembershipId, $"/organisation/pharmacies/{pharmacyId}/invitations/{invitation.MembershipId}"));
    }

    private HttpResponseMessage ListInvitations(Call call) => !Manages(call.User, call.Route["id"])
        ? FakeResponses.Problem(HttpStatusCode.Forbidden, "organisation.not-a-manager")
        : Paged(call.Query, [.. invitations.Where(invitation => invitation.Open && invitation.PharmacyId == call.Route["id"]).Select(invitation => new
        {
            membershipId = invitation.MembershipId,
            email = invitation.Email,
            jobFunction = invitation.JobFunction,
            role = "Employee",
            expiresAt = time.GetUtcNow().AddDays(7),
        })]);

    private HttpResponseMessage Accept(Call call)
    {
        var token = call.Body["token"]?.GetValue<string>();
        var invitation = invitations.SingleOrDefault(each => each.Open && each.Token == token);
        if (invitation is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "organisation.invitation-not-found");
        }

        invitation.Open = false;
        memberships.Add(new Membership(invitation.MembershipId, invitation.PharmacyId, call.User, "Employee", invitation.JobFunction));
        return Changed(FakeResponses.Ok(invitation.MembershipId));
    }

    private HttpResponseMessage DraftCourse(Call call)
    {
        if (!identity.HasRole(call.User, "editor"))
        {
            return FakeResponses.Problem(HttpStatusCode.Forbidden, "editorial.not-an-editor");
        }

        var version = new CourseVersion(Id(), Id(), 1, call.Body["title"]?.GetValue<string>() ?? string.Empty, call.User);
        versions.Add(version);
        return Changed(FakeResponses.Created(version.Id, $"/editorial/course-versions/{version.Id}"));
    }

    private HttpResponseMessage ListVersions(Call call) => Paged(call.Query, [.. versions
        .Where(version => call.Query.GetValueOrDefault("status") is not { } status || version.Status == status)
        .Select(version => new { id = version.Id, courseId = version.CourseId, number = version.Number, title = version.Title, status = version.Status })]);

    private HttpResponseMessage GetVersion(Call call)
    {
        var version = versions.SingleOrDefault(each => each.Id == call.Route["id"]);
        return version is null ? FakeResponses.Problem(HttpStatusCode.NotFound, "editorial.course-version-not-found") : FakeResponses.Ok(new
        {
            id = version.Id,
            courseId = version.CourseId,
            number = version.Number,
            title = version.Title,
            status = version.Status,
            chapters = version.Chapters.Select((chapter, index) => new { id = chapter.Id, position = index + 1, title = chapter.Title, kind = "Text", text = chapter.Text, file = (string?)null, durationSeconds = (int?)null, pdfText = (string?)null }),
            questions = version.Questions.Select((question, index) => new
            {
                id = question.Id,
                position = index + 1,
                chapterId = question.ChapterId,
                text = question.Text,
                type = "SingleChoice",
                options = question.Options.Select(option => new { id = option.Id, text = option.Text, correct = option.Correct }),
                provenance = "Human",
                origin = question.Copied ? "Copied" : "HandWritten",
                review = question.ApprovedBy is null ? "Unreviewed" : "Approved",
                reviewedBy = question.ApprovedBy,
                reviewedAt = (DateTimeOffset?)null,
                approvedByName = question.ApprovedBy,
                approvedAt = (DateTimeOffset?)null,
            }),
            blueprint = new { questions = version.Blueprint?.Questions ?? 0, passingScorePercent = version.Blueprint?.PassingScorePercent ?? 0, validityMonths = version.Blueprint?.ValidityMonths ?? 0 },
        });
    }

    private HttpResponseMessage InDraft(Call call, Func<CourseVersion, HttpResponseMessage> change)
    {
        var version = versions.SingleOrDefault(each => each.Id == call.Route["id"]);
        if (version is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "editorial.course-version-not-found");
        }

        return !identity.HasRole(call.User, "editor") ? FakeResponses.Problem(HttpStatusCode.Forbidden, "editorial.not-an-editor")
            : version.Status != "Draft" ? FakeResponses.Problem(HttpStatusCode.Conflict, "editorial.course-version-not-a-draft")
            : change(version);
    }

    private HttpResponseMessage AddChapter(Call call) => InDraft(call, version =>
    {
        var chapter = new Chapter(Id(), call.Body["title"]?.GetValue<string>() ?? string.Empty, call.Body["text"]?.GetValue<string>() ?? string.Empty);
        version.Chapters.Add(chapter);
        return Changed(FakeResponses.Created(chapter.Id, $"/editorial/course-versions/{version.Id}/chapters/{chapter.Id}"));
    });

    private HttpResponseMessage SetBlueprint(Call call) => InDraft(call, version =>
    {
        if (version.Questions.Any(question => question.ApprovedBy is not null))
        {
            // Not wrong, but the seed sets the blueprint before any approval so BlueprintChangedBy stays empty (docs/adr/0007).
            version.BlueprintChangedAfterApproval = true;
        }

        version.Blueprint = new Blueprint(
            call.Body["questions"]?.GetValue<int>() ?? 0,
            call.Body["passingScorePercent"]?.GetValue<int>() ?? 0,
            call.Body["validityMonths"]?.GetValue<int>() ?? 0);
        return Changed(FakeResponses.Ok(new { }));
    });

    private HttpResponseMessage AddQuestion(Call call) => InDraft(call, version =>
    {
        var chapterId = call.Body["chapterId"]?.GetValue<string>();
        if (version.Chapters.All(chapter => chapter.Id != chapterId))
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "editorial.chapter-not-found");
        }

        var options = (call.Body["options"]?.AsArray() ?? []).Select(option => new Option(Id(), option?["text"]?.GetValue<string>() ?? string.Empty, option?["correct"]?.GetValue<bool>() ?? false)).ToList();
        if (call.Body["type"]?.GetValue<string>() != "SingleChoice" || options.Count(option => option.Correct) != 1)
        {
            return FakeResponses.Problem(HttpStatusCode.UnprocessableEntity, "editorial.invalid-options");
        }

        var question = new Question(Id(), chapterId ?? string.Empty, call.Body["text"]?.GetValue<string>() ?? string.Empty, options, call.User, Copied: false);
        version.Questions.Add(question);
        return Changed(FakeResponses.Created(question.Id, $"/editorial/course-versions/{version.Id}/questions/{question.Id}"));
    });

    private HttpResponseMessage Approve(Call call)
    {
        var version = versions.SingleOrDefault(each => each.Id == call.Route["id"]);
        var question = version?.Questions.SingleOrDefault(each => each.Id == call.Route["questionId"]);
        if (version is null || question is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "editorial.question-not-found");
        }

        if (version.Status is not ("Draft" or "InReview"))
        {
            return FakeResponses.Problem(HttpStatusCode.Conflict, "editorial.course-version-not-in-review");
        }

        if (question.Author == call.User || question.ApprovedBy is not null)
        {
            return FakeResponses.Problem(HttpStatusCode.Conflict, question.ApprovedBy is null ? "editorial.four-eyes-approval" : "editorial.question-already-approved");
        }

        question.ApprovedBy = call.User;
        return Changed(FakeResponses.Ok(new { }));
    }

    private HttpResponseMessage Submit(Call call) => InDraft(call, version =>
    {
        if (version.Blueprint is null || version.Questions.Count < version.Blueprint.Questions || version.Chapters.Count == 0)
        {
            return FakeResponses.Problem(HttpStatusCode.Conflict, "editorial.course-version-incomplete");
        }

        version.Status = "InReview";
        version.Submitter = call.User;
        return Changed(FakeResponses.Ok(new { }));
    });

    private HttpResponseMessage Publish(Call call)
    {
        var version = versions.SingleOrDefault(each => each.Id == call.Route["id"]);
        if (version is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "editorial.course-version-not-found");
        }

        if (version.Status != "InReview" || version.Questions.Any(question => question.ApprovedBy is null))
        {
            return FakeResponses.Problem(HttpStatusCode.Conflict, "editorial.course-version-not-ready");
        }

        if (version.Submitter == call.User || version.Questions.Any(question => question.ApprovedBy == call.User))
        {
            return FakeResponses.Problem(HttpStatusCode.Conflict, "editorial.four-eyes-publish");
        }

        foreach (var earlier in versions.Where(each => each.CourseId == version.CourseId && each.Status == "Published"))
        {
            earlier.Status = "Superseded";
        }

        version.Status = "Published";
        return Changed(FakeResponses.Ok(new { }));
    }

    private HttpResponseMessage NextVersion(Call call)
    {
        var version = versions.SingleOrDefault(each => each.Id == call.Route["id"]);
        if (version is null || version.Status != "Published" || versions.Any(each => each.CourseId == version.CourseId && each.Number > version.Number))
        {
            return FakeResponses.Problem(HttpStatusCode.Conflict, "editorial.next-version-refused");
        }

        var next = new CourseVersion(Id(), version.CourseId, version.Number + 1, version.Title, call.User) { Blueprint = version.Blueprint };
        var chapters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chapter in version.Chapters)
        {
            var copy = new Chapter(Id(), chapter.Title, chapter.Text);
            chapters[chapter.Id] = copy.Id;
            next.Chapters.Add(copy);
        }

        // Copied questions come back unreviewed: whoever approves them in this version is asked again.
        foreach (var question in version.Questions)
        {
            next.Questions.Add(new Question(Id(), chapters[question.ChapterId], question.Text, [.. question.Options.Select(option => option with { Id = Id() })], question.Author, Copied: true));
        }

        versions.Add(next);
        return Changed(FakeResponses.Created(next.Id, $"/editorial/course-versions/{next.Id}"));
    }

    private HttpResponseMessage Catalog(Call call) => Paged(call.Query, [.. versions.Select(version => version.CourseId).Distinct().Select(Published).OfType<CourseVersion>()
        .Select(version => new { courseId = version.CourseId, courseVersionId = version.Id, versionNumber = version.Number, title = version.Title, validityMonths = version.Blueprint?.ValidityMonths ?? 12 })]);

    private HttpResponseMessage ComplianceMembers(Call call) => !Manages(call.User, call.Route["id"])
        ? FakeResponses.Problem(HttpStatusCode.Forbidden, "compliance.not-a-manager")
        : Paged(call.Query, [.. memberships.Where(membership => membership.PharmacyId == call.Route["id"])
            .Select(membership => new { personId = PersonOf(membership.User), name = identity.NameOf(membership.User), jobFunction = membership.JobFunction })]);

    private HttpResponseMessage Assign(Call call)
    {
        var pharmacyId = call.Route["id"];
        if (!Manages(call.User, pharmacyId))
        {
            return FakeResponses.Problem(HttpStatusCode.Forbidden, "compliance.not-a-manager");
        }

        var courseId = call.Body["courseId"]?.GetValue<string>() ?? string.Empty;
        if (CourseCopyMissing > 0 || Published(courseId) is null)
        {
            CourseCopyMissing = Math.Max(0, CourseCopyMissing - 1);
            return FakeResponses.Problem(HttpStatusCode.NotFound, "compliance.course-not-found");
        }

        var due = DateOnly.Parse(call.Body["dueDate"]?.GetValue<string>() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture);
        if (due < Today)
        {
            return FakeResponses.Problem(HttpStatusCode.UnprocessableEntity, "compliance.due-date-in-the-past");
        }

        var assigned = new JsonArray();
        var refused = new JsonArray();
        foreach (var person in (call.Body["personIds"]?.AsArray() ?? []).Select(node => node?.GetValue<string>() ?? string.Empty))
        {
            if (assignments.Any(assignment => assignment.PersonId == person && assignment.CourseId == courseId && assignment.PharmacyId == pharmacyId))
            {
                refused.Add(new JsonObject { ["personId"] = person, ["code"] = "compliance.already-assigned", ["message"] = "The course is assigned already." });
                continue;
            }

            var assignment = new Assignment(Id(), pharmacyId, person, courseId, due, call.Body["recurrenceMonths"]?.GetValue<int?>());
            assignments.Add(assignment);
            Changes++;
            assigned.Add(new JsonObject { ["personId"] = person, ["assignmentId"] = assignment.Id });
        }

        return FakeResponses.Ok(new JsonObject { ["assigned"] = assigned, ["refused"] = refused });
    }

    private string StateOf(Assignment assignment)
    {
        var user = UserOf(assignment.PersonId);
        var exam = examinations.Where(each => each.User == user && each.CourseId == assignment.CourseId).ToList();
        return certificates.Any(certificate => certificate.User == user && certificate.CourseId == assignment.CourseId) ? "Completed"
            : exam.Any(each => each.Status == "Exhausted") ? "Locked"
            : sessions.Any(session => session.User == user && session.CourseId == assignment.CourseId) ? "InProgress"
            : assignment.Due < Today ? "Overdue"
            : "Open";
    }

    private HttpResponseMessage MyAssignments(Call call) => Paged(call.Query, [.. assignments
        .Where(assignment => assignment.PersonId == PersonOf(call.User) && (call.Query.GetValueOrDefault("pharmacyId") is not { } pharmacy || assignment.PharmacyId == pharmacy))
        .Where(assignment => StateOf(assignment) != "Completed")
        .Select(assignment => new
        {
            assignmentId = assignment.Id,
            courseId = assignment.CourseId,
            courseTitle = TitleOf(assignment.CourseId),
            state = StateOf(assignment),
            dueDate = assignment.Due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            startCourseVersionId = Published(assignment.CourseId)?.Id,
            continuesSession = sessions.Any(session => session.User == call.User && session.CourseId == assignment.CourseId),
        })]);

    private HttpResponseMessage Matrix(Call call)
    {
        var pharmacyId = call.Route["id"];
        if (!Manages(call.User, pharmacyId))
        {
            return FakeResponses.Problem(HttpStatusCode.Forbidden, "compliance.not-a-manager");
        }

        if (Paging(call.Query) is not { } paging)
        {
            return FakeResponses.Problem(HttpStatusCode.BadRequest, "invalid-argument");
        }

        var (page, pageSize) = paging;

        var held = assignments.Where(assignment => assignment.PharmacyId == pharmacyId).ToList();
        var rows = memberships.Where(membership => membership.PharmacyId == pharmacyId).Select(membership => new
        {
            personId = PersonOf(membership.User),
            name = identity.NameOf(membership.User),
            jobFunction = membership.JobFunction,
            cells = held.Where(assignment => assignment.PersonId == PersonOf(membership.User)).Select(assignment => new
            {
                courseId = assignment.CourseId,
                assignmentId = assignment.Id,
                state = StateOf(assignment),
                dueDate = assignment.Due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                lastCompletedOn = (string?)null,
                validUntil = (string?)null,
            }).ToList(),
        }).ToList();
        var compliant = held.Count(assignment => StateOf(assignment) == "Completed");
        var overdue = held.Count(assignment => StateOf(assignment) == "Overdue");
        return FakeResponses.Ok(new
        {
            courses = held.Select(assignment => assignment.CourseId).Distinct().Select(course => new { courseId = course, title = TitleOf(course), assignments = held.Count(each => each.CourseId == course), compliantAssignments = 0, overdueAssignments = 0, complianceRate = (double?)null }),
            members = new { items = rows.Skip((page - 1) * pageSize).Take(pageSize), totalCount = rows.Count, page, pageSize },
            assignments = held.Count,
            compliantAssignments = compliant,
            overdueAssignments = overdue,
            complianceRate = held.Count == 0 ? (double?)null : (double)compliant / held.Count,
        });
    }

    private object SessionDto(Session session)
    {
        var version = versions.Single(each => each.Id == session.VersionId);
        return new
        {
            id = session.Id,
            courseId = session.CourseId,
            courseVersionId = session.VersionId,
            status = version.Chapters.All(chapter => session.Completed.Contains(chapter.Id)) ? "ContentCompleted" : "InProgress",
            chapters = version.Chapters.Select((chapter, index) => new { chapterId = chapter.Id, position = index + 1, kind = "Text", progressPercent = session.Completed.Contains(chapter.Id) ? 100 : 0, completed = session.Completed.Contains(chapter.Id) }),
        };
    }

    private HttpResponseMessage StartSession(Call call)
    {
        if (MembershipMissing > 0 || memberships.All(membership => membership.User != call.User))
        {
            MembershipMissing = Math.Max(0, MembershipMissing - 1);
            return FakeResponses.Problem(HttpStatusCode.Forbidden, "learning.membership-missing");
        }

        var versionId = call.Body["courseVersionId"]?.GetValue<string>();
        var version = versions.SingleOrDefault(each => each.Id == versionId && each.Status is "Published" or "Superseded");
        if (version is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "learning.course-version-not-found");
        }

        var existing = sessions.SingleOrDefault(session => session.User == call.User && session.VersionId == version.Id);
        if (existing is not null)
        {
            return FakeResponses.Ok(SessionDto(existing));
        }

        var started = new Session(Id(), call.User, version.CourseId, version.Id);
        sessions.Add(started);
        examinations.Add(new Examination(Id(), started.Id, call.User, version.CourseId, version.Id));
        return Changed(FakeResponses.Json(HttpStatusCode.Created, SessionDto(started)));
    }

    private HttpResponseMessage Progress(Call call)
    {
        var session = sessions.SingleOrDefault(each => each.Id == call.Route["id"] && each.User == call.User);
        if (session is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "learning.session-not-found");
        }

        var chapterId = call.Body["chapterId"]?.GetValue<string>() ?? string.Empty;
        if (call.Body["percent"]?.GetValue<int>() == 100 && session.Completed.Add(chapterId))
        {
            Changes++;
        }

        var exam = examinations.Single(each => each.SessionId == session.Id);
        var version = versions.Single(each => each.Id == session.VersionId);
        if (exam.Status == "Locked" && version.Chapters.All(chapter => session.Completed.Contains(chapter.Id)))
        {
            exam.Status = "Unlocked";
        }
        else if (exam.Status == "Exhausted")
        {
            // Completing the content again after exhaustion would open a new round (docs/adr/0023, 0025): a seed never does.
            exam.ProgressAfterExhaustion++;
        }

        return FakeResponses.Ok(SessionDto(session));
    }

    private static object ExaminationDto(Examination exam) => new
    {
        id = exam.Id,
        sessionId = exam.SessionId,
        courseId = exam.CourseId,
        courseVersionId = exam.VersionId,
        status = exam.Status,
        attemptsUsed = exam.AttemptsUsed,
        maxAttempts = MaxAttempts,
        currentAttempt = exam.Current is null ? null : AttemptDto(exam, exam.Current),
        lastResult = (object?)null,
    };

    private static object AttemptDto(Examination exam, Attempt attempt) => new
    {
        id = attempt.Id,
        number = exam.AttemptsUsed + 1,
        questions = attempt.Questions.Select(question => new
        {
            id = question.Id,
            type = "SingleChoice",
            text = question.Text,
            // Shuffled: the options come in the reverse of the editor's order, under ids of the attempt's own.
            options = question.Options.AsEnumerable().Reverse().Select(option => new { id = exam.OptionIds.Single(each => each.Value == option.Id && attempt.OptionIds.Contains(each.Key)).Key, text = option.Text }),
            chosenOptionIds = attempt.Answers.GetValueOrDefault(question.Id) ?? [],
        }),
    };

    private HttpResponseMessage ExaminationBySession(Call call)
    {
        var exam = examinations.SingleOrDefault(each => each.SessionId == call.Route["sessionId"] && each.User == call.User);
        return exam is null ? FakeResponses.Problem(HttpStatusCode.NotFound, "assessment.examination-not-found") : FakeResponses.Ok(ExaminationDto(exam));
    }

    private HttpResponseMessage StartAttempt(Call call)
    {
        var exam = examinations.SingleOrDefault(each => each.Id == call.Route["id"] && each.User == call.User);
        if (exam is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "assessment.examination-not-found");
        }

        if (exam.Status != "Unlocked")
        {
            return FakeResponses.Problem(HttpStatusCode.Conflict, $"assessment.examination-{exam.Status.ToLowerInvariant()}");
        }

        var version = versions.Single(each => each.Id == exam.VersionId);
        var attempt = new Attempt(Id(), [.. version.Questions.Take(version.Blueprint?.Questions ?? version.Questions.Count)]);
        foreach (var option in attempt.Questions.SelectMany(question => question.Options))
        {
            var id = Id();
            exam.OptionIds[id] = option.Id;
            attempt.OptionIds.Add(id);
        }

        exam.Current = attempt;
        exam.Status = "InAttempt";
        return Changed(FakeResponses.Json(HttpStatusCode.Created, AttemptDto(exam, attempt)));
    }

    private HttpResponseMessage Answer(Call call)
    {
        var exam = examinations.SingleOrDefault(each => each.Id == call.Route["id"] && each.User == call.User);
        var question = exam?.Current?.Questions.SingleOrDefault(each => each.Id == call.Route["questionId"]);
        if (exam?.Current is null || question is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "assessment.attempt-not-found");
        }

        var chosen = (call.Body["optionIds"]?.AsArray() ?? []).Select(node => node?.GetValue<string>() ?? string.Empty).ToList();
        if (chosen.Count != 1 || !exam.Current.OptionIds.Contains(chosen[0]) || question.Options.All(option => option.Id != exam.OptionIds[chosen[0]]))
        {
            return FakeResponses.Problem(HttpStatusCode.UnprocessableEntity, "assessment.unknown-option");
        }

        exam.Current.Answers[question.Id] = chosen;
        Changes++;
        return FakeResponses.NoContent();
    }

    private HttpResponseMessage SubmitAttempt(Call call)
    {
        var exam = examinations.SingleOrDefault(each => each.Id == call.Route["id"] && each.User == call.User);
        if (exam?.Current is null)
        {
            return FakeResponses.Problem(HttpStatusCode.NotFound, "assessment.attempt-not-found");
        }

        var version = versions.Single(each => each.Id == exam.VersionId);
        var attempt = exam.Current;
        var correct = attempt.Questions.Count(question => attempt.Answers.GetValueOrDefault(question.Id) is [var chosen] && question.Options.Single(option => option.Id == exam.OptionIds[chosen]).Correct);
        var score = attempt.Questions.Count == 0 ? 0 : correct * 100 / attempt.Questions.Count;
        var passing = version.Blueprint?.PassingScorePercent ?? 100;
        exam.AttemptsUsed++;
        exam.Current = null;
        var passed = score >= passing;
        exam.Status = passed ? "Passed" : exam.AttemptsUsed >= MaxAttempts ? "Exhausted" : "Unlocked";
        if (passed)
        {
            certificates.Add(new Certificate(Id(), call.User, version.CourseId, version.Title, Today));
        }

        return Changed(FakeResponses.Ok(new
        {
            attemptId = attempt.Id,
            number = exam.AttemptsUsed,
            scorePercent = score,
            passingScorePercent = passing,
            outcome = passed ? "Passed" : "Failed",
            chaptersToRepeat = Array.Empty<string>(),
            attemptsLeft = MaxAttempts - exam.AttemptsUsed,
        }));
    }

    private HttpResponseMessage MyCertificates(Call call) => Paged(call.Query, [.. certificates.Where(certificate => certificate.User == call.User).Select(certificate => new
    {
        certificateId = certificate.Id,
        courseTitle = certificate.Title,
        issuedOn = certificate.IssuedOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        validUntil = certificate.IssuedOn.AddMonths(12).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        validity = "Valid",
        documentReady = true,
    })]);

    private sealed record Call(string User, Dictionary<string, string> Route, Dictionary<string, string> Query, JsonNode Body);

    private sealed record Pharmacy(string Id, string Name);

    private sealed record Membership(string Id, string PharmacyId, string User, string Role, string JobFunction);

    private sealed class Invitation(string membershipId, string pharmacyId, string email, string jobFunction, string token)
    {
        public string MembershipId { get; } = membershipId;

        public string PharmacyId { get; } = pharmacyId;

        public string Email { get; } = email;

        public string JobFunction { get; } = jobFunction;

        public string Token { get; set; } = token;

        public bool Open { get; set; } = true;
    }

    private sealed class CourseVersion(string id, string courseId, int number, string title, string author)
    {
        public string Id { get; } = id;

        public string CourseId { get; } = courseId;

        public int Number { get; } = number;

        public string Title { get; } = title;

        public string Author { get; } = author;

        public string Status { get; set; } = "Draft";

        public string? Submitter { get; set; }

        public Blueprint? Blueprint { get; set; }

        public bool BlueprintChangedAfterApproval { get; set; }

        public List<Chapter> Chapters { get; } = [];

        public List<Question> Questions { get; } = [];
    }

    private sealed record Blueprint(int Questions, int PassingScorePercent, int ValidityMonths);

    private sealed record Chapter(string Id, string Title, string Text);

    private sealed record Option(string Id, string Text, bool Correct);

    private sealed record Question(string Id, string ChapterId, string Text, List<Option> Options, string Author, bool Copied)
    {
        public string? ApprovedBy { get; set; }
    }

    private sealed record Assignment(string Id, string PharmacyId, string PersonId, string CourseId, DateOnly Due, int? RecurrenceMonths);

    private sealed record Session(string Id, string User, string CourseId, string VersionId)
    {
        public HashSet<string> Completed { get; } = [with(StringComparer.Ordinal)];
    }

    private sealed class Examination(string id, string sessionId, string user, string courseId, string versionId)
    {
        public string Id { get; } = id;

        public string SessionId { get; } = sessionId;

        public string User { get; } = user;

        public string CourseId { get; } = courseId;

        public string VersionId { get; } = versionId;

        public string Status { get; set; } = "Locked";

        public int AttemptsUsed { get; set; }

        public int ProgressAfterExhaustion { get; set; }

        public Attempt? Current { get; set; }

        /// <summary>The attempts' option ids, each to the editor's option it shows.</summary>
        public Dictionary<string, string> OptionIds { get; } = [with(StringComparer.Ordinal)];
    }

    private sealed record Attempt(string Id, List<Question> Questions)
    {
        public HashSet<string> OptionIds { get; } = [with(StringComparer.Ordinal)];

        public Dictionary<string, List<string>> Answers { get; } = [with(StringComparer.Ordinal)];
    }

    private sealed record Certificate(string Id, string User, string CourseId, string Title, DateOnly IssuedOn);
}
