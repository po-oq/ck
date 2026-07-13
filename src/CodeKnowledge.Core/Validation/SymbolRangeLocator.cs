using CodeKnowledge.Core.Hashing;

namespace CodeKnowledge.Core.Validation;

public sealed record SymbolRangeMatch(int StartLine, ValidationReason Reason);

public sealed class SymbolRangeLocator
{
    private readonly IReadOnlyList<string> _normalizedLines;
    private readonly Dictionary<int, IReadOnlyDictionary<string, int>> _windowIndexes = [];

    public SymbolRangeLocator(string content)
        => _normalizedLines = ContentHasher.NormalizeSymbolLines(content);

    public SymbolRangeMatch? Find(
        string expectedHash, int windowLength, int mappedStartLine)
    {
        if (windowLength < 1 || windowLength > _normalizedLines.Count) return null;
        if (Matches(expectedHash, windowLength, mappedStartLine))
            return new(mappedStartLine, ValidationReason.SymbolHashMatchAtMappedRange);
        return WindowIndex(windowLength).TryGetValue(expectedHash, out var start)
            ? new(start, ValidationReason.SymbolHashMatchAtMovedRange) : null;
    }

    private bool Matches(string expectedHash, int length, int start)
        => start >= 1 && start + length - 1 <= _normalizedLines.Count &&
           string.Equals(expectedHash,
               ContentHasher.ComputeSymbolHash(_normalizedLines, start, start + length - 1),
               StringComparison.Ordinal);

    private IReadOnlyDictionary<string, int> WindowIndex(int length)
    {
        if (_windowIndexes.TryGetValue(length, out var cached)) return cached;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var start = 1; start <= _normalizedLines.Count - length + 1; start++)
        {
            var hash = ContentHasher.ComputeSymbolHash(
                _normalizedLines, start, start + length - 1);
            index.TryAdd(hash, start);
        }
        return _windowIndexes[length] = index;
    }
}
