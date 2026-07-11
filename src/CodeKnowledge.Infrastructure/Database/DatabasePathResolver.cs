namespace CodeKnowledge.Infrastructure.Database;

public static class DatabasePathResolver
{
    public const string EnvironmentVariable = "CODEKNOWLEDGE_DB_PATH";

    public static string Resolve(Func<string, string?>? getEnvironmentVariable = null)
    {
        var getter = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var overridePath = getter(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(AppContext.BaseDirectory, "knowledge.db")
            : overridePath;
    }
}
