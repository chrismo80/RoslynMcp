# run_tests Architecture

## Intent

`run_tests` is an agent-facing contract, not just a shell shortcut. The design should optimize first for agent adoption in tight fix loops: low-token results, familiar invocation semantics, and enough coverage that agents do not fall back to Bash for routine test execution.

The architecture must therefore preserve two properties at the same time:

- invocation should feel close to `dotnet test`
- results should be far more compact and structured than `dotnet test`

## Proposed Responsibility Split

### MCP Tool Layer

Location:

- `src/RoslynMcp.Features/Tools/Inspections/`

Responsibilities:

- expose the MCP metadata
- accept cancellation
- delegate to a service
- map `Target` and `Filter` inputs into the canonical request model
- return the canonical `RunTestsResult`

Constraints:

- no process orchestration logic in the tool class
- no report parsing logic in the tool class
- no target discovery heuristics beyond trivial normalization

### Core Contract Layer

Locations:

- `src/RoslynMcp.Core/Models/`
- `src/RoslynMcp.Core/Contracts/`

Responsibilities:

- define request and result models for test execution
- define the service contract used by the feature layer
- keep the public outcome vocabulary stable
- preserve a request shape close to `dotnet test` mental models

Recommended public service contract:

```csharp
public interface ITestInspectionService
{
    Task<RunTestsResult> RunTestsAsync(RunTestsRequest request, CancellationToken ct);
}
```

Rationale:

- `ITestInspectionService` matches the inspection classification of the tool
- `RunTestsAsync` preserves the user-facing tool language
- the tool should depend only on this contract

### Infrastructure Execution Layer

Likely location:

- `src/RoslynMcp.Infrastructure/`

Responsibilities:

- resolve the loaded solution path
- resolve the effective execution target from the request plus loaded workspace
- invoke `dotnet test`
- pass through `Target` and `Filter` semantics wherever practical
- manage run-specific report artifacts
- collect and parse JSON or TRX outputs
- normalize raw outputs into the canonical result model

Recommended internal components:

- `TestInspectionService`: orchestration entry point behind `ITestInspectionService`
- `ITestTargetResolver`: validates and normalizes the effective `dotnet test` target
- `ITestProcessRunner`: invokes `dotnet test` and captures process metadata
- `ITestArtifactManager`: allocates and cleans per-run report files/directories
- `ITestResultInterpreter`: maps JSON, TRX, and process outcomes into `RunTestsResult`

These components are internal architecture guidance, not part of the public MCP contract.

## Architectural Priorities

1. Agent acceptance over internal elegance.
2. Token-efficient result shaping over console fidelity.
3. `dotnet test` compatibility over custom selector abstractions.
4. Thin MCP tool layer over embedded process logic.

## Invocation Model

The request model should stay intentionally small:

- `Target`: optional execution target
- `Filter`: optional `dotnet test`-style narrowing expression

Interpretation rules:

- `Target = null` means the currently loaded solution
- when `Target` is provided, the implementation should pass it through to `dotnet test` whenever possible
- when `Filter` is provided, the implementation should pass it through to `dotnet test --filter` whenever possible

This keeps the tool aligned with how agents already think about test execution.

## Service Boundary

The intended dependency chain is:

`RunTestsTool` -> `ITestInspectionService` -> internal runner/resolver/interpreter components

Boundary rules:

- `RunTestsTool` knows only MCP parameter binding and the canonical request/result models
- `ITestInspectionService` owns orchestration and outcome mapping
- internal helper components may evolve without changing the tool signature
- no other MCP tool should need to know how reports are produced or parsed

This boundary keeps the external contract stable while allowing the execution strategy to change later.

## Canonical Flow

1. Read the currently loaded solution context.
2. Resolve the effective execution target:
   - explicit `Target`, if supplied and valid
   - otherwise the loaded solution path
3. Create a run-specific artifact context so previous outputs cannot contaminate the result.
4. Execute `dotnet test` against the effective target, adding `--filter` when provided.
5. Inspect outputs in this order:
   - preferred structured JSON assertion output
   - TRX fallback output
   - process exit / infrastructure failure details
6. Map the raw execution result into `RunTestsResult`.
7. Clean up temporary artifacts that are not part of the repository state.

## Execution Pipeline Responsibilities

### Target Resolution

- validate `Target`
- normalize relative paths against the loaded solution context
- reject ambiguous or invalid targets early with a clear error result

### Process Execution

- assemble `dotnet test` arguments from effective target plus optional filter
- keep invocation semantics close to native CLI behavior
- capture exit code and enough process metadata for outcome mapping

### Artifact Isolation

- create run-scoped artifact names or directories
- prefer a dedicated per-run directory under the user's temp area rather than the repository workspace
- prevent stale JSON/TRX files from previous runs from contaminating the result
- clean up temporary artifacts after interpretation

Artifact location policy:

- TRX and any tool-owned outputs should be written under a run-specific temp directory
- the workspace should not be used as the normal artifact sink
- if `JsonObserver` output cannot be redirected, the implementation should use a narrowly scoped discovery strategy rather than recursively scanning the whole workspace

FailureReport discovery policy when `JsonObserver` cannot be redirected:

- capture a pre-run snapshot of relevant `FailureReport.json` candidates in the effective target scope
- record the run start timestamp in UTC
- after test execution, re-scan the same narrow scope
- consider only reports that are new or whose `LastWriteTimeUtc` is at or after the recorded run start time
- aggregate all matching fresh reports rather than assuming exactly one file
- if no fresh JSON report is found, continue with TRX fallback

### Result Interpretation

- prefer JSON assertion output when available
- otherwise interpret TRX output
- if neither produces test failures, use process outcome plus diagnostics to classify pass, build failure, or infrastructure failure

Interpretation guard:

- stale pre-existing `FailureReport.json` files must never be treated as evidence for the current run

## Target Resolution Strategy

The architecture should accept these target shapes for the initial release:

- omitted target -> loaded solution
- solution path within the loaded solution directory
- project path within the loaded solution directory
- directory path within the loaded solution directory

Validation should be strict enough to return a clear input error, but not so clever that the tool invents an alternate targeting language.

Project names are intentionally not the primary contract because agents get deterministic paths from other Roslyn tools more reliably than names.

## Data Model Guidance

The canonical result should answer three questions for the caller:

1. Did the run complete successfully?
2. If not, was the problem a test failure, build failure, or tool/infrastructure failure?
3. What precise failure data can be used for repair?

This is why a result envelope with `Outcome` is required for the initial release.

## Outcome Mapping

- `passed`: test execution completed and no failing tests were reported
- `passed` also covers valid filtered runs where zero tests matched, with a short summary explaining that no tests matched the filter
- `test_failures`: test execution completed and at least one failing test was reported
- `build_failed`: the run did not reach a normal test result because build or discovery failed
- `infrastructure_error`: the tool could not complete due to process start, report parsing, workspace resolution, or similar execution problems
- `cancelled`: cancellation was observed and surfaced cleanly

## Design Decisions

- Prefer JSON assertion data because it carries repair-grade fields such as expected, actual, file, and line.
- Keep TRX as a compatibility fallback instead of a primary source.
- Use the loaded workspace as the authority for what to test.
- Keep the feature behind a service boundary so future enhancements such as alternate runners, richer summarization, or response tuning do not force a tool rewrite.
- Keep the invocation surface small and `dotnet test`-like rather than introducing custom class/method selector fields.
- Treat token reduction as a first-class architectural concern, not just a response-formatting nice-to-have.
- Prefer temp-owned run artifacts over workspace-owned report files.

## Result Shaping Strategy

The response should be intentionally compact by default.

That means:

- no raw console transcript in the normal result
- short summary for pass/fail/build status
- structured failure entries only
- condensed build diagnostics only

The architecture should assume that the result may be produced many times in a single repair loop, so any field included in the result must justify its token cost.

Recommended shaping rules:

- return `Failures` only as structured records, never as embedded console blocks
- return `BuildDiagnostics` only when they materially explain a non-test failure outcome
- keep `Summary` to one short sentence
- avoid duplicating the same information across `Summary`, `Failures`, and `BuildDiagnostics`

## Risks And Guards

- Fixed file names in the repository root risk stale data and collisions; isolate each run's artifacts.
- The exact output location behavior of `AssertWithIs` `JsonObserver` is not fully documented in the package README; verify it in implementation tests and avoid workspace-wide file scavenging.
- A bare list return type cannot express build failures cleanly; use an explicit result envelope.
- Parsing logic embedded in the tool class will make the MCP layer hard to maintain; keep parsing in infrastructure.
- Tool success must not be inferred only from report presence; exit conditions and missing-report cases need explicit mapping.
- A highly custom request shape would hurt adoption even if internally elegant; stay close to `dotnet test`.
- Excessive response detail will erase the product's token advantage; keep output compact by default.

## Definition Of Done

- The MCP surface returns a canonical `RunTestsResult`.
- The MCP surface accepts `Target` and `Filter` with `dotnet test`-like semantics.
- The tool is a thin adapter over a contract/service boundary.
- JSON observer failures are normalized into `TestFailure` entries.
- TRX fallback works when JSON is absent.
- Build and infrastructure failures map to explicit outcomes.
- Tests cover pass, project targeting, filter-based narrowing, JSON failure, fallback failure, and non-test failure scenarios.

## Handoff Boundary

Architect phase complete when the contract, boundaries, outcomes, and acceptance criteria above are accepted.

Stop-Signal: Architect phase complete -> handing over to coder for implementation
