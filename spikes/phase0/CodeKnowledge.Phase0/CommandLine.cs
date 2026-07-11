namespace CodeKnowledge.Phase0;

internal enum ProbeMode { Mcp, SelfCheck, ConcurrencyWorker, Invalid }

internal sealed record CommandSelection(ProbeMode Mode, string[] Arguments);

internal static class CommandLine
{
    public static CommandSelection Parse(string[] args)
    {
        if (args.Length == 0)
            return new(ProbeMode.Mcp, []);

        var mode = args[0] switch
        {
            "mcp" => ProbeMode.Mcp,
            "self-check" => ProbeMode.SelfCheck,
            "concurrency-worker" => ProbeMode.ConcurrencyWorker,
            _ => ProbeMode.Invalid
        };
        return new(mode, args[1..]);
    }
}
