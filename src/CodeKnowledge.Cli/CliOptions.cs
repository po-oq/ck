namespace CodeKnowledge.Cli;

public sealed record CliOptions(string? Subcommand, string? InputPath, string? Cwd, bool ShowHelp)
{
    public static readonly IReadOnlySet<string> KnownSubcommands =
        new HashSet<string> { "resolve", "search", "get", "save", "validate" };

    public static CliOptions Parse(string[] args)
    {
        string? subcommand = null;
        string? inputPath = null;
        string? cwd = null;
        var showHelp = args.Length == 0;

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            switch (token)
            {
                case "--help" or "-h":
                    showHelp = true;
                    break;
                case "--input" when index + 1 < args.Length:
                    inputPath = args[++index];
                    break;
                case "--cwd" when index + 1 < args.Length:
                    cwd = args[++index];
                    break;
                default:
                    // 最初の非オプショントークンをサブコマンドとして扱う
                    subcommand ??= token;
                    break;
            }
        }

        return new CliOptions(subcommand, inputPath, cwd, showHelp);
    }
}
