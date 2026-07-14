namespace CodeKnowledge.Cli;

public static class CliInputReader
{
    public static string Read(string? inputPath, TextReader standardInput)
        => string.IsNullOrWhiteSpace(inputPath)
            ? standardInput.ReadToEnd()
            : File.ReadAllText(inputPath);
}
