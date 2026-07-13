using System.Runtime.InteropServices;

namespace CodeKnowledge.Mcp.Tests;

public sealed class PublishedServerTargetTests
{
    [Fact]
    public void Resolve_WindowsX64_ReturnsWindowsTarget()
    {
        var target = PublishedServerTargetResolver.Resolve(
            OSPlatform.Windows, Architecture.X64);

        Assert.Equal("win-x64", target.RuntimeIdentifier);
        Assert.Equal("CodeKnowledge.Mcp.exe", target.ExecutableName);
    }

    [Fact]
    public void Resolve_OSXArm64_ReturnsMacTarget()
    {
        var target = PublishedServerTargetResolver.Resolve(
            OSPlatform.OSX, Architecture.Arm64);

        Assert.Equal("osx-arm64", target.RuntimeIdentifier);
        Assert.Equal("CodeKnowledge.Mcp", target.ExecutableName);
    }

    [Fact]
    public void Resolve_WindowsArm64_Throws()
    {
        Assert.Throws<PlatformNotSupportedException>(() =>
            PublishedServerTargetResolver.Resolve(
                OSPlatform.Windows, Architecture.Arm64));
    }

    [Fact]
    public void Resolve_OSXX64_Throws()
    {
        Assert.Throws<PlatformNotSupportedException>(() =>
            PublishedServerTargetResolver.Resolve(
                OSPlatform.OSX, Architecture.X64));
    }

    [Fact]
    public void Resolve_LinuxX64_Throws()
    {
        Assert.Throws<PlatformNotSupportedException>(() =>
            PublishedServerTargetResolver.Resolve(
                OSPlatform.Linux, Architecture.X64));
    }
}
