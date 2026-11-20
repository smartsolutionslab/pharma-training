# AI engineering workflow

How PharmaTraining gets built: agents take issues from GitHub, a developer decides and merges. Most of it comes from
Software Fabric's `base.fabric` pack, which `fabric new` writes into the project (P1, #1); the rest is hand-written
here until Fabric brings it. The domain spec is [`domain/README.md`](domain/README.md).

## Stages

| Stage | Who | What comes out | Where it is defined (after P1) |
| --- | --- | --- | --- |
| Plan | Developer | Spec, decisions, issue plan; one issue per agent run with `## What`, `## Done when`, `## Depends on` | `docs/domain/README.md`, the issues in milestone *MVP 1* |
| Release | `board-promote` workflow | `agent:ready` on an issue once everything under `## Depends on` is closed | `.github/workflows/board-promote.yml` (hand-written, see below) |
| Triage | `triage` agent, optional | `agent:ready` + one `lane:` label, or `needs:human` with what is missing | `.claude/agents/triage.md` |
| Claim | Scheduled Claude session per lane | The issue's `agent/<issue>` branch (the lock), `agent:claimed` | `/next-issue` skill, `.github/scripts/board/next.sh` |
| Spec and plan | `architect` agent | Spec without open questions, plan citing ADRs, atomic tasks; goes into the PR | `.claude/agents/architect.md` |
| Red | `test-writer` agent | Tests committed alone, their failing output quoted in the PR | `.claude/agents/test-writer.md`, red-first check in CI |
| Implement | `backend-` / `frontend-` / `infra-engineer` | The smallest change that turns the tests green, without editing them | `.claude/agents/*-engineer.md` |
| Verify | `/verify` skill | The change observed working in the running app, quoted under `## How it was verified` | `.claude/skills/verify` |
| Review | The other model family, via Copilot CLI | Ranked findings on the PR; a blocking one fails `cross-model-review/verdict` | `.github/workflows/cross-model-review.yml` |
| Merge | Developer | Merge on green CI and verdict; the issue closes, which releases its dependants | Branch protection |

An agent that needs a decision, or fails a phase twice, escalates with `escalate.sh` (`needs:decision`,
`agent:blocked`) and the issue goes to its lane's owner in `docs/owners.yml`. Unassigning yourself after answering
returns it to the board.

## Labels

- `lane:backend`, `lane:frontend`, `lane:infra`: which agent lane takes the issue. `context:<context>`: the bounded
  context, for filtering only.
- `agent:ready`: agents may take it. Set by `board-promote` (or a developer); never set it on an issue whose
  dependencies are open.
- `agent:claimed`, `agent:blocked`, `needs:human`, `needs:decision`: the board protocol's states (AGENTS.md).
- An issue with a human assignee belongs to that human.

## Hand-written until Fabric has it

- **Releasing by dependencies.** Fabric's board claims the lowest-numbered `agent:ready` issue and knows nothing of
  dependencies (software-fabric#849). `.github/scripts/board-promote.sh` releases an issue when every issue in the
  first paragraph of its `## Depends on` section is closed as completed, including issues in other repositories
  (checked hourly). Try it with `DRY_RUN=1 bash .github/scripts/board-promote.sh smartsolutionslab/pharma-training`.
- **P1 is blocked on the generator.** v0.8.0 cannot mix DDD and event-sourced modules (software-fabric#848); #1
  waits for it and is released by `board-promote` once it closes.
