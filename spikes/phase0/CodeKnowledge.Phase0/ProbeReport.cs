namespace CodeKnowledge.Phase0;

internal static class ProbeExitCodes
{
    public const int Success = 0;
    public const int CheckFailed = 1;
    public const int InvalidArguments = 2;
    public const int UnexpectedError = 3;
}

internal sealed record ProbeCheck(string Id, bool Passed, string Message);

internal sealed record ProbeReport(
    string Mode,
    string Status,
    string ExecutableVersion,
    IReadOnlyList<ProbeCheck> Checks,
    IReadOnlyDictionary<string, string> Details);
