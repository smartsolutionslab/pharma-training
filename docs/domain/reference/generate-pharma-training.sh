#!/usr/bin/env bash
# PharmaTraining — generate the project with Software Fabric.
# Run with fabric v0.10.0 for P1 (#1). A module of a style new to the project (learning brings event sourcing on
# Marten) is merged into it three-way (software-fabric#848), which needs a git repository with a clean working tree,
# so every step is committed before the next. Build fabric from the v0.10.0 tag as its release does:
# dotnet build src/Fabric.Cli -c Release -p:VersionSuffix=
#
# Topology: services (ADR-0017 in software-fabric, decision D7): one host per module behind a YARP edge, a database
# per service, integration events over RabbitMQ with the outbox. Needs software-fabric #703 to #708; the profile
# saas-services-vue (#708) already sets topology, bus and tenancy.
set -euo pipefail

fabric() { dotnet "${FABRIC_HOME:?set FABRIC_HOME to the software-fabric checkout}/src/Fabric.Cli/bin/Release/net11.0/fabric.dll" "$@"; }

# fabric new needs an empty directory: generate next to the repository and move the files in, keeping docs/domain.
fabric new pharma-training --profile saas-services-vue \
  --namespace SmartSolutionsLab.PharmaTraining \
  --set backend.auth=keycloak \
  --set backend.authorization=roles \
  --set backend.llm=azure-openai \
  --set backend.llm.extras=evals \
  --set module.style=ddd \
  --set module.ddd-depth=strict

cd pharma-training
step() { git add -A && git commit -qm "$1"; }
git init -q && step "fabric new"

# Supporting contexts: DDD, strict
fabric add-module organisation; step "add-module organisation"
fabric add-module editorial; step "add-module editorial"
fabric add-module certification; step "add-module certification"

# Core: Compliance is plain DDD (state + history), Learning and Assessment are event-sourced (the stream is the audit trail)
fabric add-module compliance; step "add-module compliance"
fabric add-module learning   --set module.style=event-sourced --set module.event-store=marten --set module.cqrs=logical; step "add-module learning"
fabric add-module assessment --set module.style=event-sourced --set module.event-store=marten --set module.cqrs=logical; step "add-module assessment"

# Catalog is a read model fed by CourseVersionPublished: no aggregates, so CRUD without CQRS ceremony
fabric add-module catalog --set module.style=crud --set module.cqrs=none; step "add-module catalog"
