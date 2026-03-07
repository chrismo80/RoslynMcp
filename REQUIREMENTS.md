# Audit Remediation Requirements

Date: 2026-03-07
Source: `docs/CODE_INSPECTION_AUDIT.md`

## Goal

Improve the C# code inspection tool family so results are stable enough for automation, less noisy for human exploration, and explicit about snapshot/workspace semantics.

## Requirements

### R1. Project identity semantics

- Define whether `projectId` is stable across reloads or explicitly snapshot-scoped.
- If `projectId` remains snapshot-scoped, docs and errors must say so clearly.
- The API must expose at least one recommended stable selector for automation; `projectPath` is the default candidate.
- Stale snapshot-local identifiers should fail with targeted guidance.

### R2. Human-source-first filtering

- Support filtering out generated and intermediate artifacts such as `obj/` and `bin/` by default or via a consistent filter model.
- `understand_codebase`, `list_types`, and `trace_call_flow` must support this filtering.
- Default behavior should optimize for human repo exploration.

### R3. `understand_codebase` hotspot quality

- `quick`, `standard`, and `deep` must differ mainly in breadth/depth, not by letting generated output bury handwritten hotspots.
- Hotspot ranking must remain deterministic.

### R4. Qualified-name ambiguity reduction

- When the same declaration is visible from multiple projects, prefer one canonical result or collapse duplicate candidate views.
- Keep true ambiguity when declarations are genuinely distinct.

### R5. `trace_call_flow` usability

- Transitions must show meaningful project labels for loaded-project symbols.
- Support reducing noise from framework calls, generated code, and optionally test code.
- Interactive output must remain bounded enough for CLI use.

### R6. `explain_symbol` richness

- Include a concise behavioral/responsibility summary, not just metadata.
- Identify major collaborators or impact zones when cheaply available.

### R7. `find_codesmells` consistency

- `riskLevel` values must be documented and consistent with actual output.
- Deduplicate at result level, not only anchor level.
- Categories in docs must match emitted categories.
- Truncation warnings must stay visible and clearer.

### R8. `rename_symbol` post-mutation state

- A successful rename must leave the active workspace snapshot usable for a follow-up rename without manual reload in the normal case.
- If refresh is required, the error must be explicit and actionable.

### R9. Fresh-worktree diagnostics UX

- Fresh or detached worktrees may still report diagnostics, but likely causes must be communicated.
- Diagnostics should distinguish source issues vs missing/generated/intermediate artifacts when feasible.

### R10. Documentation parity

- README and tool descriptions must document snapshot-scoped selectors, real `riskLevel` values, and filtering semantics.
- Recommended usage for automation vs interactive exploration must be explicit.

## Priorities

- `P0`: R1, R2, R5, R7, R8
- `P1`: R3, R4, R6, R9
- `P2`: further summarization ergonomics

## Acceptance Criteria

- Audit findings can be re-run and each item is fixed, intentionally changed by design, or documented behavior.
- Interactive exploration favors handwritten production code by default or via one clearly documented switch family.
- Mutation flows no longer require manual reload after an ordinary successful rename.
