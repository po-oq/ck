using CodeKnowledge.Phase0;

var selection = CommandLine.Parse(args);
if (selection.Mode is ProbeMode.ConcurrencyWorker)
{
    return await ConcurrencyWorker.RunAsync(
        selection.Arguments,
        Console.Out,
        CancellationToken.None);
}

Console.WriteLine("Hello, World!");
return ProbeExitCodes.Success;
