using CodeKnowledge.Cli;

var options = CliOptions.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine("Usage: codeknowledge <resolve|search|get|save|validate> [--input <path>] [--cwd <path>]");
    return 0;
}

// コマンド実行は後続タスクで実装する（Task 1はスキャフォールドと引数解析のみ）
await Console.Error.WriteLineAsync($"codeknowledge: subcommand '{options.Subcommand}' is not yet implemented");
return 1;
