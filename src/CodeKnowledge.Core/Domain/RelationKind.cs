namespace CodeKnowledge.Core.Domain;

public static class RelationKind
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "calls", "implements", "inherits", "reads", "writes",
        "publishes", "subscribes", "configured-by", "tested-by",
    };
}
