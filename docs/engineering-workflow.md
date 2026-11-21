# AI engineering workflow

How PharmaTraining gets built: agents take issues from GitHub, a developer decides and merges. The workflow is
Software Fabric's board protocol (`base.fabric`, ADR-0004 of the generator), generated into this project; its rules
are in [`AGENTS.md`](../AGENTS.md), its reasoning in [`adr/fabric/base-fabric.md`](adr/fabric/base-fabric.md). The
domain spec is [`domain/README.md`](domain/README.md).

## Stages

| Stage | Who | What comes out | Where it is defined |
| --- | --- | --- | --- |
| Plan | Developer | Spec, decisions, issue plan; one issue per agent run with `## What`, `## Done when`, `## Depends on` | `docs/domain/README.md`, the issues in milestone *MVP 1* |
| Triage | `triage` agent, optional | `agent:ready` + one `lane:` label, or `needs:human` with what is missing | `.claude/agents/triage.md` |
| Claim | A Claude session per lane, scheduled or started by hand | The issue's `agent/<issue>` branch (the lock), `agent:claimed`; an issue whose dependencies are open is skipped | `/next-issue` skill, `.github/scripts/board/next.sh` |
| Spec and plan | `architect` agent | Spec without open questions, plan citing ADRs, atomic tasks; goes into the PR | `.claude/agents/architect.md` |
| Red | `test-writer` agent | Tests committed alone, their failing output quoted in the PR | `.claude/agents/test-writer.md`, red-first check in CI |
| Implement | `backend-` / `frontend-` / `infra-engineer` | The smallest change that turns the tests green, without editing them | `.claude/agents/*-engineer.md` |
| Verify | `/verify` skill | The change observed working in the running app, quoted under `## How it was verified` | `.claude/skills/verify` |
| Review | The other model family, via Copilot CLI | Ranked findings on the PR; a blocking one fails `cross-model-review/verdict` | `.github/workflows/cross-model-review.yml` |
| Merge | Developer | Merge on green CI and verdict; closing the issue lets its dependants be claimed | Branch protection |

An agent that needs a decision, or fails a phase twice, escalates with `escalate.sh` (`needs:decision`,
`agent:blocked`), and the issue goes to its lane's owner in `docs/owners.yml`. Unassigning yourself after answering
returns it to the board.

## Interim review until 2026-12-01

GitHub Copilot's usage limit postpones the cross-model review until 1 December. Until then:

- A postponed review does not hold a pull request: `cross-model-review/verdict` turns `success` with "Not reviewed:
  Copilot's limit". The cut-off is `REVIEW_OPTIONAL_UNTIL` in `.github/workflows/cross-model-review.yml` (the
  repository variable of that name overrides it). From that date on, a postponed review is pending again.
- A Claude Code session that wrote none of the code reviews instead, every three hours, with the `/review-prs` skill.
  It posts its findings as a pull request review and labels the pull request `claude-review:passed` or
  `claude-review:blocking` (cloud sessions cannot write commit statuses); the lane fixes what blocks on its next
  wake. It is a same-family review, weaker than the cross-model one, and every verdict says so.
- Merge when CI is green and the pull request is labelled `claude-review:passed` for its current head (the review names
  the SHA it read).

Both are hand-written stop-gaps. Remove them at the first `fabric upgrade` after 1 December.

## Dependencies

`next.sh` reads the `## Depends on` section of an issue, up to the next heading, and skips the issue while any issue
named there (`#12`, or `owner/repo#34`) is open. So `agent:ready` can be set when an issue is written. Everything in
that section counts: put explanations that name other issues under a heading of their own (`## Notes`), or the
issue waits for them too.

## Labels

- `lane:backend`, `lane:frontend`, `lane:infra`: which lane takes the issue. `context:<context>`: the bounded context,
  for filtering only.
- `agent:ready`: agents may take it once its dependencies are closed.
- `agent:claimed`, `agent:blocked`, `needs:human`, `needs:decision`: the board protocol's states (AGENTS.md).
- An issue with a human assignee belongs to that human.
