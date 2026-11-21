using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>A job function under its own name in the endpoints and the integration events; an undefined one is a bug.</summary>
public static class JobFunctionMappings
{
    public static OrganisationJobFunction ToDto(this JobFunction jobFunction) => jobFunction switch
    {
        JobFunction.Pharmacist => OrganisationJobFunction.Pharmacist,
        JobFunction.Pta => OrganisationJobFunction.Pta,
        JobFunction.Pka => OrganisationJobFunction.Pka,
        JobFunction.Trainee => OrganisationJobFunction.Trainee,
        JobFunction.Other => OrganisationJobFunction.Other,
        _ => throw new UnreachableException($"Undefined job function {jobFunction}."),
    };

    public static JobFunction ToDomain(this OrganisationJobFunction jobFunction) => jobFunction switch
    {
        OrganisationJobFunction.Pharmacist => JobFunction.Pharmacist,
        OrganisationJobFunction.Pta => JobFunction.Pta,
        OrganisationJobFunction.Pka => JobFunction.Pka,
        OrganisationJobFunction.Trainee => JobFunction.Trainee,
        OrganisationJobFunction.Other => JobFunction.Other,
        _ => throw new UnreachableException($"Undefined job function {jobFunction}."),
    };

    public static JobFunctionV1 ToV1(this JobFunction jobFunction) => jobFunction switch
    {
        JobFunction.Pharmacist => JobFunctionV1.Pharmacist,
        JobFunction.Pta => JobFunctionV1.Pta,
        JobFunction.Pka => JobFunctionV1.Pka,
        JobFunction.Trainee => JobFunctionV1.Trainee,
        JobFunction.Other => JobFunctionV1.Other,
        _ => throw new UnreachableException($"Undefined job function {jobFunction}."),
    };
}
