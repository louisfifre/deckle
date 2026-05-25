---
name: spawn-tasks
description: When Louis invokes you to spin off parallel topics that surfaced in conversation but are off the current main thread, create independent spawned sessions via mcp__ccd_session__spawn_task. Methodology — non-directive seed prompts that name the goal and point to where information lives, trusting the spawned session to choose its own approach. Resolves cwd to the main project root, never to a worktree. Triggers on phrases like spawn tasks, crée des tâches, fork these topics, spin off ces sujets, parallel sessions, branche-moi ces sujets, lance des tâches là-dessus.
type: skill
---

## Context

A working session often drifts across multiple topics. Only one is usually the main thread; the rest are valuable but distracting. Spawning them as independent sessions lets each topic get its own focused investigation without polluting the current flow. This skill is invoked manually by Louis — it does not auto-trigger.

The challenge: a directive seed prompt (with step-by-step instructions, phase plans, prescribed approaches) propagates the directive style into the spawned agent. The spawned session ends up executing instructions blindly instead of investigating. The countermeasure: a seed prompt that names the goal and points to resources, then stops.

## Doctrine

For each topic to spawn, produce a seed prompt structured as three blocks pulled from the closed section vocabulary (see [`save-context/format.md`](../save-context/format.md)):

**Context** — the situation, the why, the upstream constraints framing the topic. One short paragraph.

**Pointers** — where the relevant information lives: file paths, skills to invoke, MCPs to consult, ADRs to read, related research notes. Explicit markdown links or paths. Never "you already know" or "look around".

**Task** — what the spawned session should investigate or produce. One verb-phrase sentence. The goal, not the method.

Use positive injunctions throughout. Write "do Y" rather than "don't do X". The spawned session absorbs the form of its seed prompt — a positive seed produces an investigative agent.

Resolve the `cwd` for `mcp__ccd_session__spawn_task` to the main project root, never to the current worktree:
- Run `git worktree list`.
- The first line is always the main worktree path.
- Pass that path as `cwd`.

For each spawn, also produce:
- `title` — under 60 chars, imperative phrase (verb + object).
- `tldr` — 1-2 plain sentences for the chip tooltip. No file paths, no code, no jargon — describes what the spawned session will do and why.
- `prompt` — the Context + Pointers + Task block. As long as needed to convey the three blocks; no longer.

## Boundaries

**Always do:**
- Resolve `cwd` via the first line of `git worktree list`.
- Structure each seed prompt as Context + Pointers + Task.
- Use positive injunctions ("do Y instead of X") rather than negative ("don't do X").
- Produce one spawn_task chip per distinct topic.
- Verify that the Pointers block is non-empty before spawning — without pointers, the spawned session will struggle.

**Ask first:**
- If multiple topics surfaced in the conversation, list them and ask Louis which to spawn before firing any.
- If a topic's resources are unclear (you cannot point to where the information lives), surface this to Louis rather than spawning a blind task.
- If a topic seems to belong to a different repo (the work clearly lives elsewhere), ask Louis before passing a `cwd` outside the current main project.

**Never do:**
- Write step-by-step instructions, phase plans, or prescribed methodologies in the seed prompt.
- Pass the current worktree as `cwd`. The spawned session lands in the main project root.
- Prescribe an approach. Name the goal; let the spawned session choose how.
- Auto-spawn without explicit invocation from Louis. This skill answers a request; it does not initiate.
- Bundle multiple topics into one spawn_task. One chip per distinct topic.

## Pointers

- [`save-context/format.md`](../save-context/format.md) — closed section vocabulary used in the seed prompt.
- `mcp__ccd_session__spawn_task` — the tool that creates the chip.
- `git worktree list` — resolves the main worktree path from any working directory.

## Examples

**Three topics from a meandering session.**

> Louis: "On parlait de la refonte HUD, mais entre-temps on a discuté du bug Pause+F12, de la migration vers OpenTelemetry, et du chantier ambient lighting. Spawn les trois."
>
> Skill resolves `cwd` via `git worktree list` (first line). Produces three spawn_task calls, one per topic.
>
> **Spawn 1**
> - title: `Investigate Pause+F12 hotkey failure`
> - tldr: `Diagnose why the Pause+F12 binding doesn't trigger on the current machine and propose a fix.`
> - prompt:
>   > **Context** — Pause+F12 hotkey doesn't trigger on the current machine. Cause unknown. Other hotkeys work, suggesting the issue is binding-specific rather than systemic.
>   >
>   > **Pointers** — `src/Deckle.Shell/HotkeyManager.cs` owns hotkey registration via `RegisterHotKey`. Invoke `microsoft-docs` skill for the RegisterHotKey Win32 reference. The project does not install a low-level hook, so the diagnosis path is OS-level binding conflict or modifier mismatch.
>   >
>   > **Task** — Investigate the failure and propose a fix.
>
> **Spawn 2**
> - title: `Scope OpenTelemetry migration of observability`
> - tldr: `Outline the cost and shape of migrating the EventSource pipeline to OpenTelemetry.`
> - prompt:
>   > **Context** — The project uses EventSource as its observability backbone (see ADR-0005, ADR-0006). OpenTelemetry has emerged as the cross-platform standard. Question: what would migration look like and what does it cost.
>   >
>   > **Pointers** — `docs/adr/0005-adoption-eventsource-pour-l-observabilite.md` and `0006-structure-diagnostics-parent-logging-telemetry-enfants.md` carry the current rationale. `src/Deckle.Diagnostics/CLAUDE.md` carries the doctrine. The OpenTelemetry .NET docs live at `https://opentelemetry.io/docs/instrumentation/net/`.
>   >
>   > **Task** — Outline a migration plan with cost estimate; do not commit to it.

**One topic, but resources unclear → ask first.**

> Louis: "Spawn-tasks ça"
> Skill: I see one topic surfaced — "improve the HUD fade-out animation". But I cannot find clear pointers to the relevant code or ADR. Where does the current fade-out logic live, and what is the desired behavior? Without a Pointers block, the spawned session will land cold.
