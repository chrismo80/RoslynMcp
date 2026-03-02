# RoslynMcp

A Model Context Protocol (MCP) server that brings Roslyn code intelligence to AI agents.

## What It Is

RoslynMcp is a .NET 8 application that exposes the power of [Roslyn](https://github.com/dotnet/roslyn) (the .NET compiler platform) through the MCP protocol. It acts as a bridge between AI assistants and your C# codebase, enabling deep code understanding and analysis.

## Why It Exists

Traditional AI code assistants often rely on simplistic pattern matching (grep/glob) which misses semantic context. RoslynMcp solves this by providing:

- **Semantic understanding** — It knows what your code *means*, not just what it *says*
- **Symbol resolution** — Understands types, methods, properties across your entire solution
- **Call graph tracing** — See how code flows through your system
- **Code smell detection** — Identifies potential issues using [Roslynator](https://github.com/dotnet/roslynator) analyzers

## What You Can Use It For

| Capability              | Description                                          |
| ----------------------- | ---------------------------------------------------- |
| **Load Solution**       | Initialize a .sln file and prepare the workspace     |
| **Understand Codebase** | Quick orientation with complexity hotspots           |
| **List Dependencies**   | Understand project relationships                     |
| **List Types**          | Discover all classes, interfaces, enums in a project |
| **List Members**        | Explore methods, properties, fields of any type      |
| **Resolve Symbols**     | Get canonical IDs for any code symbol                |
| **Explain Symbols**     | Understand what a symbol does and where it's used    |
| **Trace Call Flow**     | See upstream callers or downstream callees           |
| **Find Usages**         | Locate all references to a type/member               |
| **Find Code Smells**    | Detect potential issues in a file                    |




## Public MCP API

These are the public agent-facing tools. Descriptions are intentionally written as routing triggers so an agent prefers semantic code intelligence over generic `grep`/`glob` fallback.


### `load_solution`

MUST be called first: loads a `.sln` file and initializes the Roslyn workspace. All other tools require a loaded solution to work. Optionally accepts a solution path (absolute or workspace-relative). Returns project list and baseline diagnostics.

Parameters:
- `solutionHintPath` (optional): Optional solution hint path (absolute or workspace-relative).



### `understand_codebase`

Quick codebase orientation: returns project structure with dependencies and hotspots (most complex/commented methods). Use at session start to identify high-impact areas. Profiles: `quick` (fast), `standard` (balanced), `deep` (thorough).

Parameters:
- `profile` (optional): Hotspot profile: `quick`, `standard`, or `deep`. Defaults to `standard`; unsupported values are treated as `standard`.



### `list_dependencies`

Lists project dependencies. Returns all projects that a project depends on (`outgoing`), projects that depend on it (`incoming`), or both. Use at session start to understand project relationships. Without a specific project selector, returns dependencies across the solution.

Parameters:
- `projectPath` (optional): Project selector option 1: exact project path from `load_solution` output. Provide exactly one selector (`path`, `name`, or `id`).
- `projectName` (optional): Project selector option 2: project name from `load_solution` output.
- `projectId` (optional): Project selector option 3: projectId from `load_solution` output.
- `direction` (optional): Dependency direction: `outgoing`, `incoming`, or `both`. Defaults to `both`.



### `list_types`

Lists all source-declared types (classes, interfaces, enums, structs, records) in a project. Returns stable `symbolId`s and declaration locations for drill-down. Requires `projectPath`, `projectName`, or `projectId` from `load_solution` output. Supports filtering by namespace, kind (`class`/`interface`/`enum`/`struct`/`record`), and accessibility.

Parameters:
- `projectPath` (optional): Project selector option 1: exact project path from `load_solution` output. Provide exactly one selector (`path`, `name`, or `id`).
- `projectName` (optional): Project selector option 2: project name from `load_solution` output.
- `projectId` (optional): Project selector option 3: projectId from `load_solution` output.
- `namespacePrefix` (optional): Namespace prefix filter.
- `kind` (optional): Kind filter: `class`, `record`, `interface`, `enum`, or `struct`.
- `accessibility` (optional): Accessibility filter: `public`, `internal`, `protected`, `private`, `protected_internal`, or `private_protected`.
- `limit` (optional): Maximum results to return. Defaults to `100`; clamped to `0..500`.
- `offset` (optional): Zero-based pagination offset. Defaults to `0`.



### `list_members`

Lists members (methods, properties, fields, events, constructors) of a resolved type. Requires either `typeSymbolId` from `list_types` OR `path+line+column` pointing to a type. Supports filtering by kind, accessibility, binding (`static`/`instance`), and includes inherited members option. Returns stable `symbolId`s and signatures.

Parameters:
- `typeSymbolId` (optional): Type selector mode A: typeSymbolId from `list_types`. Use this, or provide `path+line+column`.
- `path` (optional): Type selector mode B: source file path used with `line+column`.
- `line` (optional): Type selector mode B: 1-based line number used with `path+column`.
- `column` (optional): Type selector mode B: 1-based column number used with `path+line`.
- `kind` (optional): Kind filter: `method`, `property`, `field`, `event`, or `ctor`.
- `accessibility` (optional): Accessibility filter: `public`, `internal`, `protected`, `private`, `protected_internal`, or `private_protected`.
- `binding` (optional): Binding filter: `instance` or `static`.
- `includeInherited` (optional): Include inherited members when `true`. Defaults to `false`.
- `limit` (optional): Maximum results to return. Defaults to `100`; clamped to `0..500`.
- `offset` (optional): Zero-based pagination offset. Defaults to `0`.



### `resolve_symbol`

Resolves a symbol into a canonical `symbolId`. Use this FIRST before `explain_symbol`, `trace_flow`, or `list_members`. Supports three selector modes: (A) `symbolId` lookup, (B) source position (`path+line+column`), or (C) `qualifiedName` (fully qualified or short name). Mode C requires project selector (`path`/`name`/`id`).

Parameters:
- `symbolId` (optional): Selector mode A: canonical symbolId lookup.
- `path` (optional): Selector mode B: source file path used with `line+column` lookup.
- `line` (optional): Selector mode B: 1-based line number used with `path+column`.
- `column` (optional): Selector mode B: 1-based column number used with `path+line`.
- `qualifiedName` (optional): Selector mode C: qualifiedName lookup (fully qualified or short name).
- `projectPath` (optional): Optional project selector for qualifiedName lookup: exact project path from `load_solution`.
- `projectName` (optional): Optional project selector for qualifiedName lookup: project name from `load_solution`.
- `projectId` (optional): Optional project selector for qualifiedName lookup: projectId from `load_solution`.



### `explain_symbol`

Explains a resolved symbol: its role, signature, containing namespace/type, key references (where it's used), and impact hints (zones with high reference density). Requires `symbolId` from `resolve_symbol` OR `path+line+column` pointing to the symbol.

Parameters:
- `symbolId` (optional): Symbol selector mode A: canonical symbolId. Use this, or provide `path+line+column`.
- `path` (optional): Symbol selector mode B: source file path used with `line+column`.
- `line` (optional): Symbol selector mode B: 1-based line number used with `path+column`.
- `column` (optional): Symbol selector mode B: 1-based column number used with `path+line`.



### `trace_call_flow`

Traces call flow from/to a symbol. Use to understand code flow: upstream shows callers (who uses this), downstream shows callees (what this calls). Requires `symbolId` OR `path+line+column`. Direction: `upstream`, `downstream`, or `both` (default). Depth: how many hops to traverse (default `2`, max unbounded). Returns call graph edges with locations.

Parameters:
- `symbolId` (optional): Symbol selector mode A: canonical symbolId. Use this, or provide `path+line+column`.
- `path` (optional): Symbol selector mode B: source file path used with `line+column`.
- `line` (optional): Symbol selector mode B: 1-based line number used with `path+column`.
- `column` (optional): Symbol selector mode B: 1-based column number used with `path+line`.
- `direction` (optional): Traversal direction: `upstream`, `downstream`, or `both`. Aliases `up`/`down` are accepted. Default is `both`.
- `depth` (optional): Traversal depth as a non-negative integer. Defaults to `2` when omitted; values below `1` execute as depth `1`.



### `find_usages`

Finds references/usages of a symbol within a project or the entire solution. Use to locate where a type, method, property, or field is being used. Returns reference locations with file paths and line numbers. Requires `symbolId` from `resolve_symbol`, `list_types`, or `list_members`.

Parameters:
- `symbolId` (required): Canonical symbolId from `resolve_symbol`, `list_types`, or `list_members`.
- `scope` (optional): Search scope: `project` (containing project) or `solution` (all projects, default).



### `find_codesmells`

Finds deterministic code-smell candidates in a document by probing Roslyn diagnostics and refactoring anchors.

Parameters:
- `path` (required): Source document path. The file must exist in the currently loaded solution.



## Quick Start (Recommended)

Run the server from source for the most reliable `MSBuildWorkspace` behavior.

### Prerequisites

- .NET SDK 8.0 (or the version pinned by `global.json`)
- A usable MSBuild environment (normally available with the .NET SDK)

### Installation

```bash
dotnet tool install -g RoslynMcp
```

### Update

```bash
dotnet tool update -g RoslynMcp
```


### MCP config (OpenCode)

```json
{
  "roslyn": {
    "type": "local",
    "command": [ "roslynmcp" ]
  }
}
```

_This project uses Roslynator, licensed under Apache 2.0._