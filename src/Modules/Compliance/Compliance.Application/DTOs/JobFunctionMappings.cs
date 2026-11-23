using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>A job function under its own name in the endpoints; an undefined one is a bug.</summary>
public static class JobFunctionMappings
{
    public static ComplianceJobFunction ToDto(this JobFunction jobFunction) => jobFunction switch
    {
        JobFunction.Pharmacist => ComplianceJobFunction.Pharmacist,
        JobFunction.Pta => ComplianceJobFunction.Pta,
        JobFunction.Pka => ComplianceJobFunction.Pka,
        JobFunction.Trainee => ComplianceJobFunction.Trainee,
        JobFunction.Other => ComplianceJobFunction.Other,
        _ => throw new UnreachableException($"Undefined job function {jobFunction}."),
    };

    public static JobFunction ToDomain(this ComplianceJobFunction jobFunction) => jobFunction switch
    {
        ComplianceJobFunction.Pharmacist => JobFunction.Pharmacist,
        ComplianceJobFunction.Pta => JobFunction.Pta,
        ComplianceJobFunction.Pka => JobFunction.Pka,
        ComplianceJobFunction.Trainee => JobFunction.Trainee,
        ComplianceJobFunction.Other => JobFunction.Other,
        _ => throw new UnreachableException($"Undefined job function {jobFunction}."),
    };
}
