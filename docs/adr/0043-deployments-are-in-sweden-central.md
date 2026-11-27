# 0043: Deployments are in Sweden Central

**Status:** Accepted (the owner decided on 2026-11-26). Supersedes in part ADR 0005 ("everything planned for Germany West Central"): staging and production are deployed to `swedencentral`.

## Context

- ADR 0005 planned every deployment for Germany West Central, and `deploy.yml` set `Azure__Location: germanywestcentral`.
- The first staging deploy (run 45708396904) built and pushed every image, then failed provisioning the PostgreSQL Flexible Server: `ParameterOutOfRange: The value of the 'Version' should be in: []`. Azure offers the subscription no server version there: `az postgres flexible-server list-skus -l germanywestcentral` answers "Provisioning is restricted in this region. Please choose a different region." (`OfferRestricted`). West Europe is restricted the same way; North Europe, Sweden Central, France Central, Switzerland North, Poland Central and Italy North are not.
- Lifting the restriction takes a support request and waiting for it. The owner chose to move instead.

## Decision

- **Staging and production are deployed to Sweden Central.** `deploy.yml` sets `Azure__Location: swedencentral` in both jobs and refuses a resource group that exists elsewhere; the architecture rule `DeployWorkflowDeploysStagingThenApprovedProduction` holds it to that. The AppHost still names no region (ADR 0005).
- Sweden Central is in the EU, so the data stays in the EU, as ADR 0001 and 0006 ask of the model; it is also one of the regions ADR 0001 names for an Azure OpenAI resource.

## Consequences

- A resource group made for Germany West Central is refused by `deploy.yml`. Staging's was recreated in Sweden Central, and the deploy identity's two role assignments on it with it (README, "Deploy to Azure", step 3); what the failed run had created there was deleted, its Key Vault purged so the name is free.
- Production's resource group is created in Sweden Central when production is set up.
- Moving back to Germany West Central later is a change of `deploy.yml`, the rule and this ADR, and a new resource group: Azure moves none of these resources between regions.
- The first deploy in Sweden Central failed provisioning the server with `RegionalAllocationFailed`: Aspire pins a Flexible Server to availability zone 1, and that zone had no capacity for `Standard_B2s` then, although the region offers the size in zones 1 to 3. The server is published without a zone (`DatabaseServerSize.ApplyTo`), so Azure places it in a zone that has capacity.
