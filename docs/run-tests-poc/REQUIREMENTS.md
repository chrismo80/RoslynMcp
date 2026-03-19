# run_tests Requirements

## Problem

Agents can already call `dotnet test` through a shell, but the shell path is expensive in repeated fix loops because the output is noisy and token-heavy. The main purpose of `run_tests` is to reduce token cost in workflows like `edit -> build -> test -> edit -> build -> test` while still preserving enough information for the next repair step.

Machine-readable failures matter, but they are secondary to the primary goal: making test execution cheap enough, focused enough, and complete enough that an agent prefers this tool over raw shell output.

## Goal

Provide a `run_tests` MCP tool that becomes the default agent path for common .NET test workflows by:

- drastically reducing token usage compared with raw `dotnet test` output
- returning compact, decision-ready results
- covering enough common test-targeting scenarios that an agent does not need to fall back to Bash for routine work

## Tool Identity

- MCP tool name: `run_tests`
- Classification: inspection tool
- Product position: read-only diagnostic execution for tests, with build implicitly included as part of normal `dotnet test` behavior

Although the tool executes tests, it belongs with inspection tools because it does not mutate source code and exists to produce diagnostic information for the agent.

## Primary User

- MCP clients and agents working against the loaded Roslyn workspace

## Product Principle

The most important requirement is agent acceptance.

If an agent frequently needs to abandon `run_tests` and use `dotnet test` directly for normal workflows, then the tool has failed even if its returned payload is well designed.

The tool should therefore optimize for:

- default low-token responses
- coverage of common targeting scenarios
- predictable behavior across fix loops

## Usability Principle

The tool should stay as close as practical to the mental model of `dotnet test`.

If an agent already knows how to narrow test execution with `dotnet test`, it should be able to use `run_tests` with minimal remapping.

The contract should therefore center around:

- execution target: what to run tests against
- test filter: which tests to include within that target

Expected agent behavior:

- most commonly omit `Target` and run the loaded solution
- for local iteration, pass a concrete test project path
- for narrow loops, pass a `FullyQualifiedName`-based filter

For compatibility and agent acceptance:

- `Target` should be pass-through friendly when provided
- `Filter` should be pass-through friendly and map directly to `dotnet test --filter` semantics as much as practical
- the tool should avoid inventing a parallel selector language unless a clear agent benefit justifies it

This is preferable to a highly custom request shape that is expressive on paper but forces the agent to learn a separate testing dialect.

## Core Agent Use Cases

The tool should cover the most common reasons an agent would otherwise use `dotnet test` directly:

1. Run all tests for the loaded solution after a broader change.
2. Run tests for a single test project after a localized change.
3. Run tests for a filtered subset such as one class, one method, one namespace, one category, or one trait.
4. Re-run a very narrow subset during tight repair loops without paying full-shell token cost.
5. Keep uncommon but valid selection patterns inside the tool contract rather than forcing Bash.

These use cases are part of the requirements because missing any of them increases the chance that the agent falls back to shell execution.

## In Scope

- Run tests for the currently loaded solution
- Run tests for a selected test project within the loaded solution
- Support test selection through a filter model close to `dotnet test`
- Resolve the execution target from the loaded workspace, not from the process current directory alone
- Return a deterministic structured result
- Keep default responses compact enough for frequent fix loops
- Return condensed failure and build information instead of raw runner logs by default
- Prefer rich assertion data when available from `AssertWithIs` JSON output
- Fall back to TRX-derived failure data when rich JSON output is not available
- Distinguish successful test runs from failing runs and non-test execution failures
- Avoid reusing stale report artifacts from previous runs

## Out of Scope For Initial Release

- Streaming live test progress
- Parallel orchestration across multiple solutions
- Historical storage of test runs
- Full fidelity reproduction of every test runner field
- Multiple response detail levels
- Failure count capping or truncation controls

Clarification:

- Solution and project targeting are in scope.
- Filter-based narrowing inside that target is in scope and should align closely with `dotnet test` semantics.
- Rich console log replay is out of scope unless a future detail mode requires a small excerpt.

## Functional Requirements

1. The tool shall expose an MCP tool named `run_tests`.
2. The tool shall execute tests against the currently loaded solution context.
3. The tool shall be read-only from the MCP contract perspective.
4. The tool shall return a canonical result object rather than raw console output.
5. The result shall include an outcome that lets the caller distinguish at least these states:
   - `passed`
   - `test_failures`
   - `build_failed`
   - `infrastructure_error`
   - `cancelled`
6. The request contract shall let the caller run against at least these execution targets without requiring raw CLI construction:
   - loaded solution by default
   - one project within the loaded solution
7. The request contract shall support test filtering using a model that is as close as practical to `dotnet test` filter usage.
8. The contract shall not require separate first-class fields for every narrowing scenario if a `dotnet test`-style filter already covers that scenario well.
9. When `Target` is supplied, the implementation should prefer passing that target through to `dotnet test` rather than translating it into a custom selector model.
10. When `Filter` is supplied, the implementation should prefer passing that filter through to `dotnet test --filter` rather than translating it into a custom selector model.
11. The default response shall optimize for low token usage.
12. When test failures are available, the result shall include a list of structured failure entries.
13. A failure entry should include the richest available subset of these fields:
   - test identifier or method name
   - message
   - expected value
   - actual value
   - source file path
   - source line
   - assertion code snippet or expression
   - stack trace
14. When build or discovery fails, the result shall include condensed structured diagnostics rather than only a generic failure status.
15. The tool shall prefer structured JSON assertion output when present.
16. The tool shall fall back to TRX parsing when JSON assertion output is absent.
17. The tool shall not report stale failures from earlier invocations.
18. The tool shall preserve enough information for an agent to decide the next repair step without reading terminal logs.
19. The tool shall not require the agent to infer success or failure from raw process output.

## Minimal Public Contract

The tool should move from returning only a list of failures to a request/response contract with explicit target, filter, and outcome.

```csharp
public sealed record RunTestsRequest(
    string? Target = null,
    string? Filter = null);

public sealed record RunTestsResult(
    string Outcome,
    int? ExitCode,
    IReadOnlyList<TestFailure> Failures,
    IReadOnlyList<BuildDiagnostic>? BuildDiagnostics = null,
    string? Summary = null,
    ErrorInfo? Error = null);

public sealed record TestFailure(
    string? TestName,
    string? Message,
    string? Expected,
    string? Actual,
    string? File,
    int? Line,
    string? Code,
    string? StackTrace);

public sealed record BuildDiagnostic(
    string? Id,
    string? Message,
    string? File,
    int? Line,
    int? Column,
    string? Severity);
```

Notes:

- `Target = null` should mean the currently loaded solution.
- `Target` should support at least these values for the initial release:
  - omitted or `null` for the loaded solution
  - solution-relative or absolute `.sln` / `.slnx` path when the resolved target remains inside the loaded solution directory
  - solution-relative or absolute `.csproj` path when the resolved target remains inside the loaded solution directory
  - solution-relative or absolute directory path when the resolved target remains inside the loaded solution directory
- The most likely agent-supplied `Target` value is a concrete project path, because it is deterministic and already available from other RoslynMcp tools.
- `Filter` should stay as close as practical to `dotnet test` filter semantics so agents can transfer existing habits directly.
- The most important stable filter pattern for agents is `FullyQualifiedName`, for example exact method targeting or class/namespace contains matching.
- `Target` and `Filter` should remain compatible with pass-through execution wherever practical.
- `Summary` is optional and may hold a short human-readable sentence for build or infrastructure failures.
- `Error` is reserved for invalid invocation or unrecoverable tool-level failure, consistent with the existing tool style.
- Counts such as passed or skipped are intentionally deferred unless they are trivial to add without weakening the initial release.

## Acceptance As A Product Requirement

The initial release is successful only if an agent would reasonably prefer `run_tests` over Bash for routine test loops.

That means:

- the default response must be significantly smaller than raw `dotnet test` output
- the result must still be sufficient for the next action
- common targeting and filtering scenarios must stay inside the tool
- the request model must feel familiar enough that an agent does not prefer Bash just because `dotnet test` syntax is easier to express

## Acceptance Criteria

- When all tests pass, the tool returns `Outcome = passed` and an empty `Failures` collection.
- When the caller omits a target, the tool runs against the loaded solution.
- When the caller targets a project, the tool executes only that project or returns a clear invalid-input error.
- When the caller needs a class, method, category, namespace, or similar subset that `dotnet test` normally expresses through filtering, the tool supports that use case inside the contract.
- When the caller needs a non-standard subset, the tool still offers an in-contract targeting path instead of forcing Bash.
- When one or more tests fail and JSON observer output exists, the tool returns `Outcome = test_failures` and structured failure entries populated from JSON.
- When tests fail without JSON observer output, the tool returns `Outcome = test_failures` and the best available TRX-derived failure entries.
- When compilation or discovery fails before normal test result generation, the tool returns `Outcome = build_failed` or `infrastructure_error` with condensed diagnostics instead of pretending the run passed.
- When a valid filter matches zero tests, the tool returns `Outcome = passed` with a short summary explaining that no tests matched the filter.
- The tool uses the loaded solution path as its execution anchor.
- Re-running the tool does not surface stale report files from a prior invocation.
- In the common case, the response is compact enough that repeated fix loops are materially cheaper than shell-based `dotnet test`.

## Non-Functional Requirements

- Keep the contract stable and small.
- Optimize for token efficiency and agent adoption over terminal fidelity.
- Keep the implementation easy to evolve toward a more capable runner later.

## Handoff To Coder

- Introduce a canonical request/result model for `run_tests` instead of returning only `IReadOnlyList<TestResult>?`.
- Keep the request model close to `dotnet test`: target plus filter rather than a heavily custom selector language.
- Ensure the first implementation covers the main agent targeting cases: loaded solution, project selection, and filter-based narrowing.
- Keep the default response intentionally compact.
- Add focused tests for pass, targeted execution, JSON failure, TRX fallback, and build-failure behavior.
- Do not add streaming, historical persistence, full log replay, detail modes, or failure caps in this phase.
