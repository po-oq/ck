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
}
