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
    public void FindByRepositoryRoot_matches_case_insensitively_on_windows_paths()
    {
        Store.Upsert(Sample());
        Assert.NotNull(Store.FindByRepositoryRoot(@"c:\WORK\order-api"));
    }

    [Fact]
    public void CountKnowledge_returns_zero_without_rows()
    {
        Store.Upsert(Sample());
        Assert.Equal(0, Store.CountKnowledge("github.com/company/order-api"));
    }

    public void Dispose() => _db.Dispose();
}
