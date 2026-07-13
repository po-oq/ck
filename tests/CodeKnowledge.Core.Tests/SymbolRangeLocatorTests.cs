using CodeKnowledge.Core.Hashing;
using CodeKnowledge.Core.Validation;

namespace CodeKnowledge.Core.Tests;

public sealed class SymbolRangeLocatorTests
{
    private const string Symbol = "void Send()\n{\n    Mail();\n}\n";

    [Fact]
    public void Find_prefers_the_diff_mapped_range()
    {
        var content = "header\n" + Symbol + "footer\n";
        var hash = ContentHasher.ComputeSymbolHash(content, 2, 5);
        var match = new SymbolRangeLocator(content).Find(hash, 4, 2);
        Assert.NotNull(match);
        Assert.Equal(2, match.StartLine);
        Assert.Equal(ValidationReason.SymbolHashMatchAtMappedRange, match.Reason);
    }

    [Fact]
    public void Find_scans_the_file_when_the_symbol_moved()
    {
        var target = "line1\nline2\n" + Symbol;
        var hash = ContentHasher.ComputeSymbolHash(Symbol, 1, 4);
        var match = new SymbolRangeLocator(target).Find(hash, 4, 1);
        Assert.NotNull(match);
        Assert.Equal(3, match.StartLine);
        Assert.Equal(ValidationReason.SymbolHashMatchAtMovedRange, match.Reason);
    }

    [Fact]
    public void Find_returns_null_when_the_symbol_changed()
    {
        var hash = ContentHasher.ComputeSymbolHash(Symbol, 1, 4);
        var changed = Symbol.Replace("Mail();", "Queue();", StringComparison.Ordinal);
        Assert.Null(new SymbolRangeLocator(changed).Find(hash, 4, 1));
    }

    [Fact]
    public void Find_ignores_the_approved_whitespace_differences()
    {
        var hash = ContentHasher.ComputeSymbolHash(Symbol, 1, 4);
        var whitespaceOnly = Symbol.Replace("    Mail();", "\tMail();   ",
            StringComparison.Ordinal);
        Assert.NotNull(new SymbolRangeLocator(whitespaceOnly).Find(hash, 4, 1));
    }

    [Fact]
    public void Find_treats_comment_changes_as_content_changes()
    {
        const string withComment = "void Send()\n{\n    Mail(); // old\n}\n";
        var hash = ContentHasher.ComputeSymbolHash(withComment, 1, 4);
        var changed = withComment.Replace("// old", "// new", StringComparison.Ordinal);
        Assert.Null(new SymbolRangeLocator(changed).Find(hash, 4, 1));
    }

    [Fact]
    public void Find_accepts_duplicate_windows_and_reuses_the_window_index()
    {
        var target = Symbol + "separator\n" + Symbol;
        var hash = ContentHasher.ComputeSymbolHash(Symbol, 1, 4);
        var locator = new SymbolRangeLocator(target);
        Assert.Equal(1, locator.Find(hash, 4, 99)!.StartLine);
        Assert.Equal(1, locator.Find(hash, 4, 99)!.StartLine);
    }
}
