using System.Runtime.ExceptionServices;

namespace CodeKnowledge.Phase0;

internal static class SelfCheckDatabaseCleanup
{
    public static void DeleteCandidates(string databasePath)
    {
        Exception? firstFailure = null;
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                var candidate = databasePath + suffix;
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                firstFailure ??= exception;
            }
        }

        if (firstFailure is not null)
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
    }
}
