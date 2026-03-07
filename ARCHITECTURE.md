# Audit Remediation Architecture

Date: 2026-03-07
Depends on: `REQUIREMENTS.md`

## Intent

Address the audit findings without creating a pile of unrelated one-off options.

## Decisions

### A1. Selector stability vs convenience

- Treat `projectPath` as the canonical stable project selector for automation.
- Treat Roslyn `projectId` as workspace-snapshot-local unless a custom stable identity layer is introduced.
- Add explicit stale-selector messaging tied to snapshot semantics.

### A2. Shared visibility policy

- Reuse one internal filter/classification model across tools.
- At minimum classify:
  - human source
  - generated/intermediate source
  - test source
  - external/framework/BCL symbols

### A3. Graph computation vs presentation

- Keep detailed call-graph computation separate from interactive summarization.
- Resolve project labels from symbol ownership or document/project membership, not from parsing `symbolId` strings.

### A4. `explain_symbol` as synthesis layer

- Build explanation from signature, outline, references, and cheap collaborator signals.
- Keep output deterministic and compact.

### A5. `find_codesmells` normalization pipeline

- Split discovery from public-result normalization.
- Normalize enums, deduplicate semantically equivalent findings, and document the public vocabulary.

### A6. Explicit mutation lifecycle handling

- Keep one authoritative active solution snapshot.
- After successful refactor application, update or transparently reload the snapshot before success is returned.

## Suggested Order

1. Fix selector semantics and docs.
2. Fix `trace_call_flow` project labeling.
3. Introduce shared source classification/filtering.
4. Apply filtering to hotspots and type listing.
5. Normalize `find_codesmells` contract and deduplication.
6. Harden post-rename workspace synchronization.
7. Enrich `explain_symbol`.
8. Improve fresh-worktree diagnostics UX.

## Definition of Done

- Public docs match actual emitted values and selector stability.
- Default interactive behavior is less polluted by generated/intermediate content.
- `trace_call_flow` transitions no longer report `unknown` for loaded-project symbols under normal conditions.
- Successive rename operations work without mandatory manual reload in the happy path.

Architect phase complete -> handing over to code-monkey for implementation
