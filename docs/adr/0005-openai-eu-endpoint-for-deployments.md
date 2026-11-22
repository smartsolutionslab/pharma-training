# 0005: OpenAI's EU endpoint for MVP 1 deployments

**Status:** Accepted. Supersedes in part ADR 0001 ("The resource and its deployment are in the EU"), for deployments only, until Azure OpenAI is set up in Germany West Central.

## Context

- ADR 0001 has the model served by an Azure OpenAI resource in an EU region, deployed as Data Zone Standard (EU) or regional Standard. It names OpenAI's own endpoint (`https://api.openai.com/v1`) only for content that may leave the EU.
- For #114 the owner decided (decision 2): everything is deployed to Germany West Central, and for MVP 1 the model is **OpenAI directly, not Azure OpenAI**: an OpenAI project with EU data residency, at `https://eu.api.openai.com/v1`, set through the existing parameters `azure-openai-endpoint` and `azure-openai-api-key`. Azure OpenAI in Germany West Central comes later.
- `AzureOpenAIChatClient` (`src/BuildingBlocks/Llm/Wiring`) already uses an endpoint that ends in `/v1` as is, and appends `/openai/v1/` to any other (AGENTS.md, "LLM: Azure OpenAI"). The client is the same `OpenAI` package for both.

## Decision

- **A deployment's model is an OpenAI project with EU data residency.** Its `azure-openai-endpoint` is `https://eu.api.openai.com/v1` and its `azure-openai-api-key` the project's key, a Key Vault secret like every other publish-mode secret (ADR 0004). `azure-openai-deployment` names the OpenAI model (`gpt-5-mini` by default).
- **Nothing changes in code.** No Azure OpenAI or Cognitive Services resource is provisioned (`AppHostPublishParametersTests` fails on one), no code branches on the endpoint, and no second client or provider switch is added.
- **The project's data residency settings are the owner's.** The OpenAI project is created with EU data residency and its key is set in each GitHub environment's secrets; that is part of the first deploy (#129), not of the code.
- **Run mode and the evals are unchanged.** A developer and the evals workflow may still point the parameters at an Azure OpenAI resource in the EU, as ADR 0001 says; the rest of ADR 0001 (empty defaults, `503 model-not-configured`, unconfigured integration tests) stands for every environment.

## Consequences

- A deployment's prompts and answers are processed by OpenAI in the EU under its data residency terms, not by an Azure resource in the project's own subscription. That is what the owner accepted for MVP 1; moving to Azure OpenAI in Germany West Central is setting the two parameters to that resource's endpoint and key, and this ADR is then superseded.
- Nothing checks that the endpoint is the EU one: `https://api.openai.com/v1` would work as well and process data outside the EU. Reviewing the environments' secrets and variables is where that is seen.
- The parameters keep their Azure names (`azure-openai-*`) although they name an OpenAI endpoint; renaming them is a fabric decision, not this one's.
- No fabric-generated file changes for this decision.
