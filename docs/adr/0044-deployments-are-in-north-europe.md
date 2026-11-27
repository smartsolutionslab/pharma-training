# 0044: Deployments are in North Europe

**Status:** Accepted (the owner decided on 2026-11-27). Supersedes [0043](0043-deployments-are-in-sweden-central.md): staging and production are deployed to `northeurope`.

## Context

- ADR 0043 moved the deployments from Germany West Central, where the subscription may not create a PostgreSQL Flexible Server (`OfferRestricted`), to Sweden Central.
- Both staging deploys to Sweden Central (runs 45957416429 and 46047022164) built and pushed every image, then failed provisioning the server: `RegionalAllocationFailed: We do not have sufficient capacity for the requested compute size in this region.` The first pinned the server to zone 1 (Aspire's default); the second named no zone (`DatabaseServerSize.ApplyTo`) and failed the same way.
- Quota was not the cause: the subscription used 0 of its 8 B-series cores there. The region lacked the hardware for this subscription at the time, which can last for days.
- In North Europe the subscription already runs a Burstable Flexible Server, so Azure has provided that size there.

## Decision

- **Staging and production are deployed to North Europe** (Ireland, in the EU). `deploy.yml` sets `Azure__Location: northeurope` in both jobs and refuses a resource group that exists elsewhere; the architecture rule `DeployWorkflowDeploysStagingThenApprovedProduction` holds it to that. The AppHost still names no region (ADR 0005), and the server still names no zone.
- The data stays in the EU, as ADR 0001 and 0006 ask.

## Consequences

- Staging's resource group was recreated in North Europe, with the deploy identity's two role assignments on it (README, "Deploy to Azure", step 3); what the failed runs had created in Sweden Central was deleted, its Key Vault purged so the name is free.
- Production's resource group is created in North Europe when production is set up.
- Moving to another region later is a change of `deploy.yml`, the rule and a new ADR, and a new resource group: Azure moves none of these resources between regions.
