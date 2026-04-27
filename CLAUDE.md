# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at `specs/001-ledger-crud/plan.md`.
<!-- SPECKIT END -->

> The block above is rewritten by `/speckit-plan` to point at the active feature's `plan.md`. Do not delete the `SPECKIT START` / `SPECKIT END` markers — the planning workflow locates them by exact match.

## What this repo is

A Spec-Driven Development (SDD) workspace scaffolded with Spec Kit (`speckit`) v0.8.2.dev0, integrated with Claude Code. There is no application source yet — work is done feature-by-feature via the SDD slash commands. Each feature produces design artifacts under `specs/<NNN-short-name>/` before any code is written at the repo root.

## SDD workflow (the core loop)

Run these slash commands in order. Each one reads artifacts from the previous step and the project constitution:

1. `/speckit-constitution` — author/update `.specify/memory/constitution.md` (currently a placeholder; populate before relying on Constitution Check gates in `/speckit-plan`).
2. `/speckit-specify "<feature description>"` — generates a 2–4 word short name, creates `specs/<prefix>-<short-name>/spec.md`, persists the resolved path to `.specify/feature.json`, and writes a quality checklist at `specs/<...>/checklists/requirements.md`.
3. `/speckit-clarify` *(optional)* — asks up to 5 targeted questions and folds answers back into `spec.md`.
4. `/speckit-plan` — runs `.specify/scripts/bash/setup-plan.sh --json`, reads the spec + constitution, and produces `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`. Also rewrites the `SPECKIT START`/`END` block in this file to point at the new plan.
5. `/speckit-tasks` — generates dependency-ordered `tasks.md`.
6. `/speckit-analyze` *(optional)* — cross-artifact consistency check across spec, plan, tasks.
7. `/speckit-implement` — executes `tasks.md`. Requires `plan.md` and `tasks.md` to exist.

`/speckit-checklist` and `/speckit-taskstoissues` (sync to GitHub) are auxiliary.

## How feature scope is resolved

`.specify/scripts/bash/common.sh::get_feature_paths` resolves the active feature directory in this priority order:

1. `SPECIFY_FEATURE_DIRECTORY` env var (explicit override).
2. `.specify/feature.json` `feature_directory` key (written by `/speckit-specify`).
3. Branch-name prefix lookup against `specs/` (e.g. branch `004-foo` → `specs/004-*`).

This means the spec directory and the git branch are decoupled — `feature.json` is the source of truth once `/speckit-specify` has run. Multiple branches can target the same spec (handy for fixes on top of an existing feature).

Branch naming is enforced by `check_feature_branch`: `NNN-slug` (3+ digits) or `YYYYMMDD-HHMMSS-slug`. `branch_numbering` is set to `sequential` in `.specify/init-options.json`.

## Three-layer architecture

- **`.claude/skills/`** — Slash-command surface for Claude Code. One `SKILL.md` per command (`speckit-specify`, `speckit-plan`, `speckit-tasks`, `speckit-implement`, `speckit-clarify`, `speckit-analyze`, `speckit-constitution`, `speckit-checklist`, `speckit-taskstoissues`, plus `speckit-git-*` from the git extension). The SKILL.md files are the prompts — read them when a command misbehaves.
- **`.specify/`** — Spec Kit runtime:
  - `scripts/bash/` — `common.sh` (path resolution, branch validation, template composition), `create-new-feature.sh`, `setup-plan.sh`, `check-prerequisites.sh`. Skills shell out to these via `--json` and parse the result.
  - `templates/` — `spec-template.md`, `plan-template.md`, `tasks-template.md`, `checklist-template.md`, `constitution-template.md`. `common.sh::resolve_template` supports a 4-tier override chain: project `templates/overrides/` → installed presets (priority-sorted via `.registry`) → extensions → core.
  - `memory/constitution.md` — project principles, loaded by `/speckit-plan` for the Constitution Check gate.
  - `extensions/git/` — bundled git extension (commands, hooks, config). See below.
  - `extensions.yml` — registers `before_*` / `after_*` hooks per phase. `before_constitution` and `before_specify` are mandatory; the rest are optional auto-commit prompts.
  - `workflows/speckit/workflow.yml` — the bundled "Full SDD Cycle" workflow chaining specify → plan → tasks → implement with review gates.
  - `feature.json` — written by `/speckit-specify`; pins the active feature directory.
- **`specs/<prefix>-<slug>/`** — per-feature artifacts: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, `contracts/`, `tasks.md`, `checklists/`. This directory does not exist until the first `/speckit-specify`.

## Hook contract (read before editing skills)

Every speckit skill begins and ends with a Pre/Post-Execution Check that scans `.specify/extensions.yml` for `before_<phase>` / `after_<phase>` hooks. Conventions enforced across all skills:

- Hook command names use dots (`speckit.git.commit`); when emitting them as Claude slash commands, replace dots with hyphens (`/speckit-git-commit`).
- Hooks with `enabled: false` are skipped. Hooks with a non-empty `condition:` are deferred to the HookExecutor (skills must not evaluate conditions).
- Mandatory hooks (`optional: false`) emit `EXECUTE_COMMAND:` and must be awaited before continuing. Optional hooks emit a prompt the user can run.

If you add or modify a phase, mirror this pattern — every `SKILL.md` for the speckit family follows the same template.

## Common operations

```bash
# Inspect the active feature's resolved paths (no validation):
.specify/scripts/bash/check-prerequisites.sh --paths-only --json

# Validate that plan.md exists for the current feature (used by /speckit-tasks):
.specify/scripts/bash/check-prerequisites.sh --json

# Validate that plan.md AND tasks.md exist (used by /speckit-implement):
.specify/scripts/bash/check-prerequisites.sh --json --require-tasks --include-tasks

# Pin a feature directory without changing branches:
SPECIFY_FEATURE_DIRECTORY=specs/003-user-auth .specify/scripts/bash/check-prerequisites.sh --paths-only

# Dry-run a feature branch + spec dir name without creating anything:
.specify/scripts/bash/create-new-feature.sh --dry-run --short-name user-auth "Add user authentication"
```

`jq` is preferred for JSON output but all scripts have `python3` and shell fallbacks. PyYAML is required only for non-`replace` template composition strategies; without it, layered presets fall back to top-priority replace.

## Working effectively in this repo

- **Don't hand-edit `specs/<feature>/plan.md`, `tasks.md`, etc.** — re-run the corresponding `/speckit-*` command so the workflow stays consistent with the templates.
- **Don't hand-create feature directories.** `/speckit-specify` writes `.specify/feature.json`; downstream commands trust that file.
- **Constitution gates fail loudly.** If `/speckit-plan` errors on Constitution Check, fix `.specify/memory/constitution.md` first — it currently still contains `[PLACEHOLDER]` tokens from the template.
- **Auto-commit hooks fire between phases.** When a slash command finishes, expect a prompt to commit; declining is fine but leaves the working tree dirty for the next phase's `before_*` hook to ask again.
