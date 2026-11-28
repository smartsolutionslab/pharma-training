# 0045: Deployments are in France Central

**Status:** Accepted (the owner decided on 2026-11-28). Supersedes [0044](0044-deployments-are-in-north-europe.md): staging and production are deployed to `francecentral`.

## Context

- ADR 0044 moved the deployments to North Europe, where the subscription could create the PostgreSQL Flexible Server that Sweden Central had no capacity for.
- The staging deploy to North Europe (run 46055600263) created the server, the Key Vault, the registry, the network and every identity, then failed creating the Container Apps environment: `MaxNumberOfRegionalEnvironmentsInSubExceeded: The subscription cannot have more than 1 Container App Environments in North Europe.` The subscription's limit is one environment per region (`ManagedEnvironmentCount`), and another application's environment holds North Europe's.
- In France Central the subscription has no environment yet (`ManagedEnvironmentCount: 0/1`) and 16 B-series cores for PostgreSQL (`standardBSFamily: 0/16`). Azure offers `Standard_B2s` there in zones 1 to 3; whether it has the capacity shows only when the server is created.

## Decision

- **Staging and production are deployed to France Central** (Paris, in the EU). `deploy.yml` sets `Azure__Location: francecentral` in both jobs and refuses a resource group that exists elsewhere; the architecture rule `DeployWorkflowDeploysStagingThenApprovedProduction` holds it to that. The AppHost still names no region (ADR 0005), and the server still names no zone.
- The data stays in the EU, as ADR 0001 and 0006 ask.

## Consequences

- Staging deploys to a new resource group in France Central, `rg-pharma-training-staging-fc`, named by the `staging` environment's variable `AZURE_RESOURCE_GROUP`, with the deploy identity's two role assignments on it (README, "Deploy to Azure", step 3). North Europe's group stays until France Central's deploy succeeds, so the move can be undone.
- **One environment per region is not enough for both environments.** Production's environment needs the limit in France Central raised to 2 (a quota request, "Service and subscription limits (quotas)", Azure Container Apps, Managed Environment Count) before production is set up.
- Moving to another region later is a change of `deploy.yml`, the rule and a new ADR, and a new resource group: Azure moves none of these resources between regions.
