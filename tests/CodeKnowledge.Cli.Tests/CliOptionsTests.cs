using CodeKnowledge.Cli;

namespace CodeKnowledge.Cli.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Parse_reads_subcommand_and_options()
    {
        var options = CliOptions.Parse(["save", "--input", "payload.json", "--cwd", "C:/repo"]);
        Assert.Equal("save", options.Subcommand);
        Assert.Equal("payload.json", options.InputPath);
        Assert.Equal("C:/repo", options.Cwd);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void Parse_detects_help_after_subcommand()
    {
        var options = CliOptions.Parse(["search", "--help"]);
        Assert.Equal("search", options.Subcommand);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_detects_top_level_help_with_no_subcommand()
    {
        var options = CliOptions.Parse(["--help"]);
        Assert.Null(options.Subcommand);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_treats_empty_args_as_help()
    {
        var options = CliOptions.Parse([]);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void KnownSubcommands_are_the_five_use_cases()
    {
        Assert.Equal(
            new HashSet<string> { "resolve", "search", "get", "save", "validate" },
            CliOptions.KnownSubcommands.ToHashSet());
    }

    [Fact]
    public void Parse_treats_dangling_input_flag_as_subcommand_without_crashing()
    {
        // 特性テスト: "--input"の直後に値トークンが無い場合、現状の実装は
        // (index+1 < args.Length)ガードが偽になりdefaultケースへ落ちて
        // サブコマンド名として扱われる。ここでは既存の挙動を固定するだけで、
        // 挙動自体の変更は行わない（別途対応予定の既知の課題）。
        var options = CliOptions.Parse(["--input"]);
        Assert.Equal("--input", options.Subcommand);
        Assert.Null(options.InputPath);
        Assert.Null(options.Cwd);
        Assert.False(options.ShowHelp);
    }
}
