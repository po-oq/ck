using System.Runtime.InteropServices;

namespace CodeKnowledge.Mcp.Tests;

internal readonly record struct PublishedServerTarget(
    string RuntimeIdentifier, string ExecutableName);

internal static class PublishedServerTargetResolver
{
    public static PublishedServerTarget ResolveCurrent()
    {
        var os = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OSPlatform.Create(RuntimeInformation.OSDescription);
        return Resolve(os, RuntimeInformation.ProcessArchitecture);
    }

    public static PublishedServerTarget Resolve(OSPlatform os, Architecture cpu)
    {
        if (os == OSPlatform.Windows && cpu == Architecture.X64)
            return new("win-x64", "CodeKnowledge.Mcp.exe");
        if (os == OSPlatform.OSX && cpu == Architecture.Arm64)
            return new("osx-arm64", "CodeKnowledge.Mcp");
        throw new PlatformNotSupportedException(
            $"Supported E2E targets: WINDOWS/X64, OSX/Arm64; detected {os}/{cpu}.");
    }
}
