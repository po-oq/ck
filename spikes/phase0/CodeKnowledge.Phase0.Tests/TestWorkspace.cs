namespace CodeKnowledge.Phase0.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "ck-phase0-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string PathFor(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort test cleanup must not replace the assertion or probe result.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup must not replace the assertion or probe result.
        }
    }
}
