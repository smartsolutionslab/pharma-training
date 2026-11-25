namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>The demo plan of #457 as data: what it seeds, and that everything in it is named as demo data.</summary>
public class SeedPlanTests
{
    private const string Hygiene = "Demo: Hygiene in der Apotheke";

    private const string Btm = "Demo: BtM-Dokumentation";

    private const string Dsgvo = "Demo: Datenschutz (DSGVO) im Apothekenalltag";

    private const string FirstAid = "Demo: Erste Hilfe / Notfallmanagement";

    private const string Markt = "Demo-Apotheke am Markt";

    private const string Rosen = "Demo-Apotheke Rosenstraße";

    private const string Loewen = "Demo-Löwen-Apotheke Großstadt";

    [Fact]
    public void The_plan_has_three_pharmacies_four_courses_and_one_second_version()
    {
        var plan = SeedPlan.Demo;

        plan.Pharmacies.Select(pharmacy => (pharmacy.Name, pharmacy.Manager, pharmacy.Employees.Count)).ShouldBe(
            [(Markt, "demo-markt-leitung", 4), (Rosen, "demo-rosen-leitung", 3), (Loewen, "demo-loewen-leitung", 22)],
            ignoreOrder: true);
        plan.Courses.Select(course => (course.Title, course.Chapters.Count, course.Chapters.Sum(chapter => chapter.Questions.Count), course.Blueprint)).ShouldBe(
            [
                (Hygiene, 3, 4, new DemoBlueprint(4, 80, 12)),
                (Btm, 3, 4, new DemoBlueprint(4, 80, 12)),
                (Dsgvo, 3, 4, new DemoBlueprint(4, 80, 12)),
                (FirstAid, 4, 5, new DemoBlueprint(5, 80, 24)),
            ],
            ignoreOrder: true);
        var revised = plan.Courses.Where(course => course.SecondVersionChapters.Count > 0).ShouldHaveSingleItem("Exactly one course has a second version.");
        revised.Title.ShouldBe(Hygiene);
        revised.SecondVersionChapters.ShouldHaveSingleItem("The second version adds one chapter.").Questions.Count.ShouldBe(1, "The added chapter brings one question.");

        plan.Users.Count.ShouldBe(35, "3 editors, 3 managers and 29 employees.");
        plan.Users.Select(user => user.Username).ShouldBeUnique();
        plan.Editors.ShouldBe(new DemoEditors("demo-autorin", "demo-pruefer", "demo-freigabe"));
        plan.Users.Where(user => user.RealmRole == "editor").Select(user => user.Username).ShouldBe(["demo-autorin", "demo-pruefer", "demo-freigabe"], ignoreOrder: true);
        plan.Users.Where(user => user.RealmRole is not null and not "editor").ShouldBeEmpty("Only the editors hold a realm role.");

        var invited = plan.Pharmacies.SelectMany(pharmacy => pharmacy.Employees).Select(employee => employee.Username).ToList();
        invited.Count.ShouldBe(29, "29 invitations, each address once (docs/adr/0004).");
        invited.ShouldBeUnique();
    }

    [Fact]
    public void The_plan_drives_every_state_the_issue_asks_for()
    {
        var plan = SeedPlan.Demo;
        int Learners(Func<DemoScenario, bool> which) => plan.Scenarios.Where(which).Sum(scenario => scenario.Learners.Count);

        Learners(scenario => scenario.Outcome == DemoOutcome.Passed).ShouldBe(10, "Ten certificates: Markt 2 (Hygiene v1) + 1 (DSGVO), Rosen 1 (BtM), Löwen 6 (Hygiene v2).");
        Learners(scenario => scenario.Outcome == DemoOutcome.Failed).ShouldBe(1, "rosen-2 fails one attempt.");
        Learners(scenario => scenario.Outcome == DemoOutcome.Exhausted).ShouldBe(1, "rosen-3 exhausts the DSGVO examination.");
        Learners(scenario => scenario.Outcome == DemoOutcome.Started).ShouldBe(5, "markt-3 starts BtM, loewen-07..10 start Hygiene.");
        Learners(scenario => scenario.DueInDays == 0).ShouldBe(4, "markt-4 and the three Rosen employees are due today, overdue from the next day (gap G1).");
        Learners(scenario => scenario.Pharmacy == Loewen).ShouldBe(44, "All 22 Löwen employees get Hygiene and Erste Hilfe.");
        plan.Scenarios.Where(scenario => scenario.RecurrenceMonths is not null)
            .ShouldHaveSingleItem("One assignment recurs (docs/adr/0026).")
            .ShouldSatisfyAllConditions(
                scenario => scenario.Course.ShouldBe(Dsgvo),
                scenario => scenario.RecurrenceMonths.ShouldBe(12),
                scenario => scenario.Outcome.ShouldBe(DemoOutcome.Passed));
        plan.Scenarios.Where(scenario => scenario.Course == Hygiene && scenario.CourseVersion == 1)
            .SelectMany(scenario => scenario.Learners).ShouldBe(["demo-markt-1", "demo-markt-2"], ignoreOrder: true, "Markt passes Hygiene v1 before v2 is published.");
        plan.Scenarios.ShouldAllBe(scenario => scenario.DueInDays >= 0, "Compliance refuses a due date in the past; nothing is backdated.");
    }

    [Fact]
    public void Every_name_in_the_plan_is_marked_as_demo_data()
    {
        var plan = SeedPlan.Demo;

        plan.Users.ShouldAllBe(user => user.Username.StartsWith("demo-", StringComparison.Ordinal));
        plan.Users.ShouldAllBe(user => user.Email == user.Username + "@example.com");
        plan.Pharmacies.ShouldAllBe(pharmacy => pharmacy.Name.StartsWith("Demo-", StringComparison.Ordinal));
        plan.Courses.ShouldAllBe(course => course.Title.StartsWith("Demo: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_question_has_three_options_one_of_them_correct()
    {
        var questions = SeedPlan.Demo.Courses
            .SelectMany(course => course.Chapters.Concat(course.SecondVersionChapters))
            .SelectMany(chapter => chapter.Questions)
            .ToList();

        questions.ShouldNotBeEmpty();
        questions.ShouldAllBe(question => question.Options.Count == 3 && question.Options.Count(option => option.Correct) == 1);
        questions.ShouldAllBe(question => question.Options.Select(option => option.Text).Distinct().Count() == 3, "The seeder matches options by text.");
    }

    [Fact]
    public void Every_scenario_names_people_pharmacies_and_courses_of_the_plan()
    {
        var plan = SeedPlan.Demo;
        var users = plan.Users.Select(user => user.Username).ToHashSet(StringComparer.Ordinal);

        plan.Pharmacies.ShouldAllBe(pharmacy => users.Contains(pharmacy.Manager) && pharmacy.Employees.All(employee => users.Contains(employee.Username)));
        foreach (var scenario in plan.Scenarios)
        {
            var pharmacy = plan.Pharmacies.SingleOrDefault(each => each.Name == scenario.Pharmacy).ShouldNotBeNull($"{scenario} names no pharmacy of the plan.");
            scenario.Learners.ShouldAllBe(learner => pharmacy.Employees.Any(employee => employee.Username == learner), $"{scenario} names a learner who is not an employee of {pharmacy.Name}.");
            var course = plan.Courses.SingleOrDefault(each => each.Title == scenario.Course).ShouldNotBeNull($"{scenario} names no course of the plan.");
            scenario.CourseVersion.ShouldBeInRange(1, course.SecondVersionChapters.Count > 0 ? 2 : 1, $"{scenario} names a version the course does not have.");
        }
    }
}
