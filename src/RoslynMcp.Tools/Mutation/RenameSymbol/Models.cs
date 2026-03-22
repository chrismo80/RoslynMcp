using RoslynMcp.Tools.Mutation.Shared;

namespace RoslynMcp.Tools.Mutation.RenameSymbol;

public sealed record Request(string SymbolId, string NewName);
public sealed record ReferencePosition(int Line, int Column);
public sealed record AffectedFileLocations(string FilePath, IReadOnlyList<ReferencePosition> Locations);
public sealed record Result(string? RenamedSymbolId, int ChangedDocumentCount, IReadOnlyList<AffectedFileLocations> AffectedLocationFiles, IReadOnlyList<string> ChangedFiles, ErrorInfo? Error = null);
