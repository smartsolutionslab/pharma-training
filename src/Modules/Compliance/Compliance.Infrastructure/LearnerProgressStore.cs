using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure;

/// <summary>
/// The session starts and certificates Compliance keeps (docs/adr/0024). The module's unit of work saves what is added;
/// nothing is ever updated, so two deliveries of one event race only on its primary key.
/// </summary>
public sealed class LearnerProgressStore(ComplianceDbContext context) : ILearnerProgressStore
{
    public Task<bool> HasSessionStartAsync(LearningSessionId session, CancellationToken cancellationToken) =>
        context.Set<LearningSessionStart>().AnyAsync(start => start.Id == session, cancellationToken);

    public void Add(LearningSessionStart start) => context.Add(start);

    public Task<bool> HasCertificateAsync(CertificateId certificate, CancellationToken cancellationToken) =>
        context.Set<IssuedCertificate>().AnyAsync(issued => issued.Id == certificate, cancellationToken);

    public void Add(IssuedCertificate certificate) => context.Add(certificate);

    public async Task<IReadOnlyList<LearnerProgress>> OfLearnerAsync(PersonId learner, IReadOnlyCollection<CourseId> courses, CancellationToken cancellationToken)
    {
        Ensure.That(learner).IsNotNull();
        Ensure.That(courses).IsNotNull();

        List<CourseId> asked = [.. courses.Distinct()];
        var starts = await context.Set<LearningSessionStart>()
            .AsNoTracking()
            .Where(start => start.Learner == learner && asked.Contains(start.Course))
            .ToListAsync(cancellationToken);
        var certificates = await context.Set<IssuedCertificate>()
            .AsNoTracking()
            .Where(certificate => certificate.Learner == learner && asked.Contains(certificate.Course))
            .ToListAsync(cancellationToken);
        var startsOf = starts.ToLookup(start => start.Course);
        var certificatesOf = certificates.ToLookup(certificate => certificate.Course);

        return [.. asked.Select(course => LearnerProgress.Of(learner, course, [.. startsOf[course]], [.. certificatesOf[course]]))];
    }
}
