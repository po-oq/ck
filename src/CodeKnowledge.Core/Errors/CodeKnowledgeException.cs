namespace CodeKnowledge.Core.Errors;

public sealed class CodeKnowledgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public const string GitRepositoryRequired = "git_repository_required";
    public const string GitNotFound = "git_not_found";
    public const string InvalidArguments = "invalid_arguments";
    public const string FactRequiresEvidence = "fact_requires_evidence";
    public const string KnowledgeNotFound = "knowledge_not_found";
    public const string SchemaVersionUnsupported = "schema_version_unsupported";
    public const string DatabaseBusy = "database_busy";
    public const string InternalError = "internal_error";
}
