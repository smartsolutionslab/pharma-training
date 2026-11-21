using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Stores a membership Organisation started, once. A membership starts once, so a member already stored means its event
/// was handled, whatever the event's identifier or payload: a second delivery changes nothing, and the first one wins.
/// </summary>
public sealed class MembershipStartedV1Consumer(IMemberStore members, IComplianceUnitOfWork unitOfWork)
{
    public async Task HandleAsync(MembershipStartedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();
        ConsumerGuards.IsText(message.DisplayName, nameof(message.DisplayName));
        Ensure.That((int)message.JobFunction, nameof(message.JobFunction)).Satisfies(value => Enum.IsDefined((JobFunctionV1)value), "is no job function Compliance knows.");
        Ensure.That((int)message.Role, nameof(message.Role)).Satisfies(value => Enum.IsDefined((MembershipRoleV1)value), "is no role Compliance knows.");

        var membership = MembershipId.From(message.MembershipId);
        if (await members.ExistsAsync(membership, cancellationToken))
        {
            return;
        }

        members.Add(Member.Started(
            membership,
            PersonId.From(message.PersonId),
            PharmacyId.From(message.PharmacyId),
            MemberName.From(message.DisplayName),
            ToJobFunction(message.JobFunction),
            ToRole(message.Role)));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private static JobFunction ToJobFunction(JobFunctionV1 jobFunction) => jobFunction switch
    {
        JobFunctionV1.Pharmacist => JobFunction.Pharmacist,
        JobFunctionV1.Pta => JobFunction.Pta,
        JobFunctionV1.Pka => JobFunction.Pka,
        JobFunctionV1.Trainee => JobFunction.Trainee,
        JobFunctionV1.Other => JobFunction.Other,
        _ => throw new InvalidOperationException($"Job function {jobFunction} has no counterpart in Compliance."),
    };

    private static MembershipRole ToRole(MembershipRoleV1 role) => role switch
    {
        MembershipRoleV1.Manager => MembershipRole.Manager,
        MembershipRoleV1.Employee => MembershipRole.Employee,
        _ => throw new InvalidOperationException($"Role {role} has no counterpart in Compliance."),
    };
}
