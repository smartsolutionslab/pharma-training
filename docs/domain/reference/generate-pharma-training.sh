#!/usr/bin/env bash
# PharmaTraining — generate the project with Software Fabric.
# Draft, not yet run: validate first with the same --set flags.
#
# Topology: services (ADR-0017 in software-fabric, decision D7): one host per module behind a YARP edge, a database
# per service, integration events over RabbitMQ with the outbox. Needs software-fabric #703 to #708; the profile
# saas-services-vue (#708) already sets topology, bus and tenancy.
set -euo pipefail

fabric() { dotnet "${FABRIC_HOME:?set FABRIC_HOME to the software-fabric checkout}/src/Fabric.Cli/bin/Release/net11.0/fabric.dll" "$@"; }

# fabric new needs an empty directory: generate next to the repository and move the files in, keeping docs/domain.
fabric new pharma-training --profile saas-services-vue \
  --set backend.auth=keycloak \
  --set backend.authorization=roles \
  --set backend.llm=azure-openai \
  --set module.style=ddd \
  --set module.ddd-depth=strict

cd pharma-training

# Supporting contexts: DDD, strict
fabric add-module organisation
fabric add-module editorial
fabric add-module certification

# Core: Compliance is plain DDD (state + history), Learning and Assessment are event-sourced (the stream is the audit trail)
fabric add-module compliance
fabric add-module learning   --set module.style=event-sourced --set module.event-store=marten --set module.cqrs=logical
fabric add-module assessment --set module.style=event-sourced --set module.event-store=marten --set module.cqrs=logical

# Catalog is a read model fed by CourseVersionPublished: no aggregates, so CRUD without CQRS ceremony
fabric add-module catalog --set module.style=crud --set module.cqrs=none
