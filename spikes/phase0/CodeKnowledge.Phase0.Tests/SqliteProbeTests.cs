namespace CodeKnowledge.Phase0.Tests;

public sealed class SqliteProbeTests
{
    [Fact]
    public void Run_VerifiesAllSqliteAssumptions()
    {
        using var workspace = new TestWorkspace();
        var report = SqliteProbe.Run(workspace.PathFor("self-check.db"));

        Assert.Equal("self-check", report.Mode);
        Assert.Equal("ok", report.Status);
        Assert.NotEmpty(report.ExecutableVersion);
        Assert.All(report.Checks, check => Assert.True(check.Passed, check.Message));
        Assert.Equal(
            [
                "sqlite.version",
                "sqlite.fts5-trigram",
                "search.fts-mail",
                "search.like-specification",
                "search.like-confirmation",
                "search.mixed",
                "sqlite.journal-mode",
                "sqlite.busy-timeout",
                "sqlite.foreign-keys"
            ],
            report.Checks.Select(static check => check.Id));
        Assert.True(Version.Parse(report.Details["sqliteVersion"]) >= new Version(3, 34, 0));
        Assert.Equal("wal", report.Details["journalMode"]);
        Assert.Equal("5000", report.Details["busyTimeout"]);
        Assert.Equal("1", report.Details["foreignKeys"]);
    }

    [Fact]
    public void Run_WhenDatabaseCannotBeOpened_ReturnsFailedRuntimeCheck()
    {
        using var workspace = new TestWorkspace();

        var report = SqliteProbe.Run(workspace.Root);

        Assert.Equal("failed", report.Status);
        var check = Assert.Single(report.Checks);
        Assert.Equal("sqlite.runtime", check.Id);
        Assert.False(check.Passed);
        Assert.Contains("SqliteException", check.Message, StringComparison.Ordinal);
    }
}
