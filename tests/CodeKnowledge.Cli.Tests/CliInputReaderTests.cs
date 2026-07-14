using CodeKnowledge.Cli;

namespace CodeKnowledge.Cli.Tests;

public sealed class CliInputReaderTests
{
    [Fact]
    public void Reads_json_from_stdin_when_no_input_path()
    {
        using var stdin = new StringReader("""{"workingDirectory":"C:/repo"}""");
        var text = CliInputReader.Read(inputPath: null, stdin);
        Assert.Contains("C:/repo", text);
    }

    [Fact]
    public void Reads_json_from_input_file_and_ignores_stdin()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ck-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{"from":"file"}""");
        try
        {
            using var stdin = new StringReader("""{"from":"stdin"}""");
            var text = CliInputReader.Read(file, stdin);
            Assert.Contains("file", text);
            Assert.DoesNotContain("stdin", text);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Preserves_newlines_from_input_file()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ck-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{\"summary\":\"line1\\nline2\"}");
        try
        {
            using var stdin = new StringReader("");
            var text = CliInputReader.Read(file, stdin);
            Assert.Contains("line1\\nline2", text); // JSONエスケープされた改行がそのまま渡る
        }
        finally
        {
            File.Delete(file);
        }
    }
}
