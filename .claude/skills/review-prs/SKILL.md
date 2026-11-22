---
name: review-prs
description: Interim review step until the cross-model review is back (Copilot's limit, until 2026-12-01). Review every open agent or side pull request whose head has no Claude review yet, post the findings, and label the verdict. Never pushes, approves or merges.
---

# Review the open agent pull requests

Hand-written, not generated: the generated review (`cross-model-review.yml`) runs on GitHub Copilot, whose usage limit
postpones it until 2026-12-01, and until then a postponed review does not hold a pull request
(`REVIEW_OPTIONAL_UNTIL`). This skill reviews in its place, in a session that wrote none of the code.

It is a **same-family** review: Claude reviews what Claude wrote. It catches less than a reviewer of another model
family would (ADR-0004), so it is a stop-gap, and every verdict says so.

## For each pull request

1. **Find the work.** List the open pull requests whose head branch starts with `agent/` (a lane's issue) or `claude/`
   (a lane's side PR: review follow-ups and fixes; the owner decided on 2026-11-22 that they get the same review):
   `gh api "repos/smartsolutionslab/pharma-training/pulls?state=open&per_page=100"`. Skip one when:
   - one of its reviews already names its head SHA (`gh api repos/smartsolutionslab/pharma-training/pulls/<n>/reviews`,
     a body starting `Claude review` that contains the SHA), so that head is reviewed;
   - its `cross-model-review/verdict` says `Reviewed by …`, so the real cross-model review ran.
2. **Read fresh.** `git fetch origin <branch>` and read the diff: `git diff origin/main...<sha>`. Read `AGENTS.md`
   on `main` first; the change is held to those rules. Read the linked issue (`Closes #n`) for what it had to do,
   and the pull request's `## How it was verified` and red-first sections.
3. **Review the diff, not its description.** Use the reviewers in `.claude/agents/` as subagents, each handed the
   diff and the issue (not your conclusions): `backend-reviewer` for `src/` and `tests/`, `frontend-reviewer` for
   `web/`, and `security-reviewer` always. Each returns findings in the shape of
   `.github/review-findings.schema.json` (file, line or null, `blocking` or `optional`, message, how to reproduce).
   Check each finding against the code yourself before you keep it; drop what you cannot confirm.
4. **Post the findings** as one pull request review with `event: COMMENT` (the account that opened the pull
   request cannot approve or request changes on it): `gh api -X POST repos/smartsolutionslab/pharma-training/pulls/<n>/reviews`,
   with a body that lists every finding, blocking ones first, and inline `comments` on their lines where the diff
   has them. Start the body with `Claude review (same family, interim until 2026-12-01)` and the head SHA.
   End it with the footer the session's instructions give.
5. **Label the verdict.** Cloud sessions cannot write commit statuses (the proxy answers 403), so the verdict is a
   label on the pull request: remove both `claude-review:passed` and `claude-review:blocking`, then add
   `claude-review:blocking` when any finding blocks, otherwise `claude-review:passed`
   (`gh api -X POST repos/smartsolutionslab/pharma-training/issues/<n>/labels -f 'labels[]=claude-review:passed'`).
   A new push makes the label stale until the next run reviews the new head.

## Rules

- Never push, never approve, never merge, never edit a pull request's code or description. The lane that opened the
  pull request fixes what blocks; the owner merges.
- Review each head once. A new push is a new head and is reviewed again on the next run.
- Findings are about the change: say what is wrong and how to reproduce it, not who wrote it.
- A finding you are unsure of is `optional`, and says why it might matter.
- When nothing is left to review, say so in one line and stop.
