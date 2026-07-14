using System.Text.Json;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Cli;

public static class ExitCodes
{
    public const int Success = 0;
    public const int InputError = 1;
    public const int PreconditionError = 2;

    // 入力を直せば解消するエラーは1、環境・前提側の実行不能状態は2へ振り分ける。
    private static readonly HashSet<string> InputErrorCodes =
    [
        CodeKnowledgeException.InvalidArguments,
        CodeKnowledgeException.FactRequiresEvidence,
        CodeKnowledgeException.KnowledgeNotFound,
    ];

    public static int ForException(Exception exception) => exception switch
    {
        JsonException => InputError,
        CodeKnowledgeException domain when InputErrorCodes.Contains(domain.Code) => InputError,
        _ => PreconditionError,
    };
}
