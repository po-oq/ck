namespace CodeKnowledge.Cli;

public static class HelpText
{
    public const string Top = """
        code-knowledge <command> [--input <file.json>] [--cwd <dir>]

        Reads one JSON object from stdin (or --input <file>) and writes a JSON
        result to stdout. Logs go to stderr. Exit: 0 ok, 1 input error, 2 precondition.

        Commands:
          resolve   Resolve the current Git repository into a project
          search    Search saved knowledge (input: {"keywords":[...],"limit":10})
          get       Get a knowledge entry (input: {"knowledgeId":"..."})
          save      Save an investigation result (input: full knowledge JSON)
          validate  Validate knowledge against HEAD or a commit (input: {"knowledgeId":"..."})

        Run `code-knowledge <command> --help` for the input JSON shape.
        """;

    public static string For(string subcommand) => subcommand switch
    {
        "resolve" => """resolve — input: {} (working dir from --cwd or current directory)""",
        "search" => """search — input: {"keywords":["mail","spec"],"limit":10}""",
        "get" => """get — input: {"knowledgeId":"<id>","versionId":"<optional>"}""",
        "save" => """
            save — input: {
              "canonicalKey":"domain.x","title":"...","originalQuestion":"...",
              "summary":"multi\nline ok","confidence":"high|medium|low",
              "evidence":[{"filePath":"src/x.cs","symbolName":"X.Y","symbolKind":"method","startLine":1,"endLine":4}],
              "facts":[{"text":"...","evidenceIndexes":[0]}],
              "inferences":[],"relations":[]
            }
            """,
        "validate" => """validate — input: {"knowledgeId":"<id>","targetCommit":"<optional>"}""",
        _ => Top,
    };
}
