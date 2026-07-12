using CodeKnowledge.Core.Domain;
using CodeKnowledge.Infrastructure.Stores;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class SqliteProjectStoreTests : IDisposable
{
    private readonly TestDatabase _db = new TestDatabase().Migrated();
    private SqliteProjectStore Store => new(_db.Factory);

    private static Project Sample(string id = "github.com/company/order-api",
        string root = @"C:\work\order-api")
        => new(id, "order-api", root, id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public void Upsert_then_FindById_roundtrips()
    {
        Store.Upsert(Sample());
        var found = Store.FindById("github.com/company/order-api");
        Assert.NotNull(found);
        Assert.Equal(@"C:\work\order-api", found.RepositoryRoot);
    }

    [Fact]
    public void Upsert_overwrites_existing_row()
    {
        Store.Upsert(Sample());
        Store.Upsert(Sample() with { RepositoryRoot = @"C:\new\clone" });
        Assert.Equal(@"C:\new\clone",
            Store.FindById("github.com/company/order-api")!.RepositoryRoot);
    }

    [Fact]
    public void FindStaleByRepositoryRoot_matches_case_insensitively_on_windows_paths()
    {
        Store.Upsert(Sample());
        Assert.Single(Store.FindStaleByRepositoryRoot(@"c:\WORK\order-api", "some-other-id"));
    }

    [Fact]
    public void FindStaleByRepositoryRoot_returns_all_non_current_rows_newest_first()
    {
        var root = @"C:\work\order-api";
        Store.Upsert(new Project(
            "local:3fa2b8c1d4e5f607", "order-api", root, null,
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-2)));
        Store.Upsert(new Project(
            "git.example.local/team/order-api", "order-api", root,
            "git.example.local/team/order-api",
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(-1)));
        Store.Upsert(Sample());

        var stale = Store.FindStaleByRepositoryRoot(root, "github.com/company/order-api");

        Assert.Equal(2, stale.Count);
        Assert.Equal("git.example.local/team/order-api", stale[0].ProjectId);
        Assert.Equal("local:3fa2b8c1d4e5f607", stale[1].ProjectId);
    }

    [Fact]
    public void CountKnowledge_returns_zero_without_rows()
    {
        Store.Upsert(Sample());
        Assert.Equal(0, Store.CountKnowledge("github.com/company/order-api"));
    }

    public void Dispose() => _db.Dispose();
}
