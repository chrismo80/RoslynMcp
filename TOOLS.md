# Tool descriptions

| Tool | Description | Parameters | Outputs |
| ---- | ----------- | ---------- | ------- |
| `load_solution` | Use this tool when you need to start working with a .NET solution and no solution has been loaded yet. This must be the first tool called in a session before any code analysis or navigation tools can be used. | `solutionHintPath` (optional): Absolute path to a `.sln` file, or to a directory used as the recursive discovery root for `.sln`/`.slnx` files. If omitted, the tool auto-detects from the current workspace. | Loaded solution path plus discovered projects, including each project's references and reverse references. |
| `load_project` | Use this tool when you need to list types declared in a specific project. It is useful for project-scoped discovery, for finding type symbols before follow-up calls such as `load_type` or `load_member`. | `projectPath`: Exact path to a project file (`.csproj`), obtained from `load_solution`. | Type count plus project-local types, with each type entry and its member count. |
| `load_type` | Use this tool when you need to inspect type hierarchy and members declared by the specific type. | `typeSymbolId`: The stable symbol ID of a type, obtained from `load_project`. | The resolved type symbol, optional documentation summary, derived types, implementations, and declared handwritten members. |
| `load_member` | Use this tool when you need callers/calles or overrides/implementations of a symbol. | `memberSymbolId`: The stable symbol ID, obtained from `load_type`. | The resolved member symbol, documentation, direct callers, direct callees, overrides, and implementations. |
| `run_tests` | Default .NET test runner. Use this instead of `dotnet test` unless you need unsupported CLI behavior. | `target` (optional): Execution target. Omit to run the currently loaded solution. Supports solution-relative or absolute `.sln`, `.slnx`, `.csproj`, or directory paths when the resolved target stays within the loaded solution directory.<br><br>`filter` (optional): `dotnet test` filter expression, passed through where practical. | Test run outcome, exit code, summary, grouped test failures, optional build diagnostics, and aggregated test counts. |

# System Prompt

```
For C# code, Prefer the `roslyn` tools over bash
```