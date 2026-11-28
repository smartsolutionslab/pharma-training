# 0046: Deployments are in Frankfurt (Germany West Central)

**Status:** Accepted (the owner decided on 2026-11-28). Supersedes [0044](0044-deployments-are-in-north-europe.md): staging and production are deployed to `germanywestcentral`, as ADR 0005 first planned.

## Context

- ADR 0005 planned every deployment for Germany West Central. The subscription may not create a PostgreSQL Flexible Server there (`OfferRestricted`: "Provisioning is restricted in this region … open a support request with Issue type of 'Service and subscription limits'"), so ADR 0043 to 0045 tried Sweden Central (no capacity), North Europe (the server was created; the Container Apps environment was refused, `MaxNumberOfRegionalEnvironmentsInSubExceeded`, although the quota API showed the limit raised to 20) and France Central (`SkuNotAvailable`).
- On 2026-11-28 the restriction in Germany West Central still stood. The subscription has no Container Apps environment there (`ManagedEnvironmentCount: 0/1`) and 40 B-series cores for PostgreSQL (`standardBSFamily: 0/40`).
- The owner chose Frankfurt: the data stays in Germany, and every region tried instead failed for a reason of its own.

## Decision

- **Staging and production are deployed to Germany West Central.** `deploy.yml` sets `Azure__Location: germanywestcentral` in both jobs and refuses a resource group that exists elsewhere; the architecture rule `DeployWorkflowDeploysStagingThenApprovedProduction` holds it to that. The AppHost still names no region (ADR 0005), and the server still names no zone.
- **The subscription needs two exceptions from Azure**, requested with "Service and subscription limits (quotas)": access to PostgreSQL Flexible Server in Germany West Central, and, before production, a Container Apps environment limit of 2 there. Until the first is granted, a deploy creates everything up to the server and fails there.

## Consequences

- Staging deploys to a new resource group in Germany West Central, `rg-pharma-training-staging-gwc`, named by the `staging` environment's variable `AZURE_RESOURCE_GROUP`, with the deploy identity's two role assignments on it (README, "Deploy to Azure", step 3). North Europe's group stays until Frankfurt's deploy succeeds.
- Moving to another region later is a change of `deploy.yml`, the rule and a new ADR, and a new resource group: Azure moves none of these resources between regions.
