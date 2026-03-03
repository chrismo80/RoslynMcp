![plot](assets/icon.png)



# RoslynMcp

A Model Context Protocol (MCP) server that brings Roslyn code intelligence to AI agents.


## Get It on NuGet

[![NuGet](https://img.shields.io/nuget/v/RoslynMcp.svg)](https://www.nuget.org/packages/RoslynMcp/)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://www.nuget.org/packages/RoslynMcp/)

_This project uses Roslynator, licensed under Apache 2.0._

#### Installation

```bash
dotnet tool install -g RoslynMcp
```

#### Update

```bash
dotnet tool update -g RoslynMcp
```


#### MCP config (OpenCode)

```json
  "mcp": {
    "code inspection": {
      "type": "local",
      "command": [
        "roslynmcp"
      ]
    }
  }
```


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
| **Find Implementations**         | Locate all implementaions of a interface or abstract class/method               |
| **Get Type Hierarchy**  | Explore type inheritance and derived types           |
| **Find Code Smells**    | Detect potential issues in a file                    |




## Public MCP API

These tool descriptions are written as routing triggers. Use them to help an agent decide which tool to call based on the user's intent.


### `load_solution`

Use this tool when you need to start working with a .NET solution and no solution has been loaded yet. This must be the first tool called in a session before any code analysis or navigation tools can be used.

Parameters:
- `solutionHintPath` (optional): Absolute path to the `.sln` file. If not provided, the tool will attempt to auto-detect a solution file.


### `understand_codebase`

Use this tool when you need a quick overview of the codebase structure at the start of a session. It returns the project structure with dependency relationships and identifies "hotspots" — the most complex and heavily-commented methods that are likely worth attention.

Parameters:
- `profile` (optional): Analysis depth. `quick` for fast results, `standard` for balanced output, `deep` for thorough analysis. Defaults to `standard`.


### `list_dependencies`

Use this tool when you need to understand how projects relate to each other within a solution. It shows the dependency graph between projects, indicating which projects depend on which others.

Parameters:
- `projectPath` (optional): Exact path to a project file (`.csproj`). Specify only one of `projectPath`, `projectName`, or `projectId`.
- `projectName` (optional): Name of a project. Specify only one of `projectPath`, `projectName`, or `projectId`.
- `projectId` (optional): Project identifier from `load_solution` output. Specify only one of `projectPath`, `projectName`, or `projectId`.
- `direction` (optional): Which direction of dependencies to return. `outgoing` shows what the selected project depends on. `incoming` shows what depends on the selected project. `both` returns both directions. Defaults to `both`.


### `list_types`

Use this tool when you need to discover all types (classes, interfaces, enums, structs, records) defined in a specific project. This is useful when you want to explore what's available in a project or find a specific type by name.

Parameters:
- `projectPath` (optional): Exact path to a project file (`.csproj`). Specify only one of `projectPath`, `projectName`, or `projectId`.
- `projectName` (optional): Name of a project. Specify only one of `projectPath`, `projectName`, or `projectId`.
- `projectId` (optional): Project identifier from `load_solution` output. Specify only one of `projectPath`, `projectName`, or `projectId`.
- `namespacePrefix` (optional): Filter to only types in namespaces starting with this prefix.
- `kind` (optional): Filter by type kind: `class`, `record`, `interface`, `enum`, or `struct`.
- `accessibility` (optional): Filter by accessibility: `public`, `internal`, `protected`, `private`, `protected_internal`, or `private_protected`.
- `limit` (optional): Maximum number of results to return. Defaults to `100`, maximum `500`.
- `offset` (optional): Number of results to skip for pagination. Defaults to `0`.


### `list_members`

Use this tool when you need to see what members (methods, properties, fields, events, constructors) exist inside a specific type. This helps you understand the structure and capabilities of a class or interface.

Parameters:
- `typeSymbolId` (optional): The stable symbol ID of a type, obtained from `list_types`. Provide this OR `path`+`line`+`column`.
- `path` (optional): Path to a source file. Provide this together with `line` and `column` instead of `typeSymbolId`.
- `line` (optional): Line number (1-based) pointing to a type in the source file.
- `column` (optional): Column number (1-based) pointing to a type in the source file.
- `kind` (optional): Filter by member kind: `method`, `property`, `field`, `event`, or `ctor`.
- `accessibility` (optional): Filter by accessibility: `public`, `internal`, `protected`, `private`, `protected_internal`, or `private_protected`.
- `binding` (optional): Filter by binding type: `static` or `instance`.
- `includeInherited` (optional): When `true`, includes members from base classes. Defaults to `false`.
- `limit` (optional): Maximum number of results to return. Defaults to `100`, maximum `500`.
- `offset` (optional): Number of results to skip for pagination. Defaults to `0`.


### `resolve_symbol`

Use this tool when you have a location in source code (file path + line + column) or a qualified name and you need to obtain a stable `symbolId` that can be used by other navigation tools. This is often the first step before calling `explain_symbol`, `trace_call_flow`, or `find_usages`.

Parameters:
- `symbolId` (optional): An existing symbol ID to look up. Provide this OR `path`+`line`+`column` OR `qualifiedName`.
- `path` (optional): Path to a source file. Provide this together with `line` and `column` instead of `symbolId` or `qualifiedName`.
- `line` (optional): Line number (1-based) in the source file.
- `column` (optional): Column number (1-based) in the source file.
- `qualifiedName` (optional): A fully qualified or short type/member name (e.g., `System.String` or `MyClass.MyMethod`). Provide this instead of `symbolId` or `path`+`line`+`column`.
- `projectPath` (optional): Required when using `qualifiedName` — path to a project that contains the symbol.
- `projectName` (optional): Required when using `qualifiedName` — name of a project that contains the symbol.
- `projectId` (optional): Required when using `qualifiedName` — project ID from `load_solution` that contains the symbol.


### `explain_symbol`

Use this tool when you need to understand what a specific symbol (type, method, property, field, etc.) does, what its signature looks like, and where it is used in the codebase. It provides a human-readable explanation along with impact hints showing areas with high reference density.

Parameters:
- `symbolId` (optional): The stable symbol ID, obtained from `resolve_symbol`, `list_types`, or `list_members`. Provide this OR `path`+`line`+`column`.
- `path` (optional): Path to a source file. Provide this together with `line` and `column` instead of `symbolId`.
- `line` (optional): Line number (1-based) pointing to the symbol in the source file.
- `column` (optional): Column number (1-based) pointing to the symbol in the source file.


### `trace_call_flow`

Use this tool when you need to understand how code flows through your system — either finding what calls a specific symbol (upstream) or what a symbol calls (downstream). This is essential for debugging, impact analysis, and understanding architectural patterns.

Parameters:
- `symbolId` (optional): The stable symbol ID, obtained from `resolve_symbol`, `list_types`, or `list_members`. Provide this OR `path`+`line`+`column`.
- `path` (optional): Path to a source file. Provide this together with `line` and `column` instead of `symbolId`.
- `line` (optional): Line number (1-based) pointing to the symbol in the source file.
- `column` (optional): Column number (1-based) pointing to the symbol in the source file.
- `direction` (optional): Which direction to trace. `upstream` finds callers (who uses this). `downstream` finds callees (what this calls). `both` returns both directions. Defaults to `both`.
- `depth` (optional): How many levels of the call chain to traverse. Defaults to `2`. Use larger values for deeper analysis, or `null` for unlimited depth.


### `find_usages`

Use this tool when you need to find all places where a specific symbol is referenced across a project or the entire solution. This is critical before refactoring or modifying any symbol to understand its impact.

Parameters:
- `symbolId` (required): The stable symbol ID, obtained from `resolve_symbol`, `list_types`, or `list_members`.
- `scope` (optional): The search scope. `project` searches only within the containing project. `solution` searches the entire solution. Defaults to `solution`.


### `find_implementations`

Use this tool when you need to find all implementations of an interface, abstract class, or abstract/virtual method. This is essential for understanding polymorphism — where interfaces are implemented or where abstract members are overridden.
    
Parameters:
- `symbolId` (required):The stable symbol ID of an interface, abstract class, or abstract/virtual method, obtained from `resolve_symbol`, `list_types`, or `list_members`.


### `get_type_hierarchy`

Use this tool when you need to understand the inheritance relationships of a type — its base classes, implemented interfaces, and any derived types. This helps you understand type evolution and polymorphism in your codebase.

Parameters:
- `symbolId` (required): The stable symbol ID of a type, obtained from `resolve_symbol`, `list_types`, or `list_members`. Must resolve to a type (class, interface, enum, struct, or record).
- `includeTransitive` (optional): When `true` (default), includes all transitive base types and all derived types. When `false`, returns only immediate parents and children.
- `maxDerived` (optional): Maximum number of derived types to return. Defaults to `200`. Higher values may impact performance.


### `find_codesmells`

Use this tool when you need to check a specific file for potential code quality issues. It runs Roslyn-based static analysis to detect common problems such as dead code, performance anti-patterns, naming violations, and other code smells identified by Roslynator analyzers.

Parameters:
- `path` (required): Path to the source file to analyze. The file must exist in the currently loaded solution.

