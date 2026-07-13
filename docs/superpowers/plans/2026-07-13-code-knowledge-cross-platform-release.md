# CodeKnowledge Cross-Platform Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Support Windows x64 and Apple Silicon Mac, test native publishes on both, and attach both distributions to tag-driven GitHub Releases.

**Architecture:** Keep MCP and SQLite behavior shared. Isolate OS differences in lexical path normalization, E2E publish-target selection, workflow runners, and archive packaging.

**Tech Stack:** .NET SDK 10.0.201, C# 14, xUnit v3, Git, SQLitePCLRaw, GitHub Actions, GitHub CLI

## Global Constraints

- Targets: Windows 11 x64/`win-x64` and macOS 15+ arm64/`osx-arm64`.
- Framework-dependent single-file output; .NET 10 Runtime required.
- No Linux, Intel Mac, Windows Arm64, self-contained output, signing, notarization, schema changes, or OS-based test skips.
- Preserve MCP contracts, evidence paths, remote IDs, and `CODEKNOWLEDGE_DB_PATH`.
- CI uses `windows-2025` and arm64 `macos-15`.
- Release assets: Windows ZIP, Mac tar.gz, and `SHA256SUMS`.

---

### Task 1: Host-independent project paths

**Files:**
- Create: `src/CodeKnowledge.Core/Projects/ProjectPathNormalizer.cs`
- Modify: `src/CodeKnowledge.Core/Projects/ProjectIdResolver.cs`
- Test: `tests/CodeKnowledge.Core.Tests/ProjectIdResolverTests.cs`

**Interfaces:** Produce `NormalizeForLocalId(string)` and `GetDisplayName(string)`; preserve `ProjectIdResolver.Resolve`.

- [ ] Add tests proving Unix trailing slash is ignored, Unix case is preserved, and `C:\\work\\my-tool` has display name `my-tool` on every host.
- [ ] Run `dotnet test tests/CodeKnowledge.Core.Tests --configuration Release`; expect current Mac failures and Unix-case failure.
- [ ] Create this implementation:

```csharp
namespace CodeKnowledge.Core.Projects;

internal static class ProjectPathNormalizer
{
    public static string NormalizeForLocalId(string path)
    {
        var value = Trim(path).Replace('\\', '/');
        return IsWindows(path) ? value.ToLowerInvariant() : value;
    }

    public static string GetDisplayName(string path)
    {
        var value = Trim(path);
        var index = value.LastIndexOfAny(['/', '\\']);
        return index >= 0 ? value[(index + 1)..] : value;
    }

    private static string Trim(string path)
    {
        var end = path.Length;
        while (end > 1 && path[end - 1] is '/' or '\\') end--;
        return path[..end];
    }

    private static bool IsWindows(string path)
        => path.Contains('\\') ||
           (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
}
```

- [ ] Replace the resolver's local normalization with `ProjectPathNormalizer.NormalizeForLocalId(context.RepositoryRoot)` and final display-name expression with `ProjectPathNormalizer.GetDisplayName(context.RepositoryRoot)`.
- [ ] Re-run Core tests; expect PASS.
- [ ] Commit: `git commit -m "fix: normalize project paths across platforms"`.

---

### Task 2: Canonical Git-root assertions

**Files:**
- Modify: `tests/CodeKnowledge.Infrastructure.Tests/TestGitRepo.cs`
- Modify: `tests/CodeKnowledge.Infrastructure.Tests/GitCliRepositoryTests.cs`

**Interfaces:** Add `RunAt(string workingDirectory, params string[] arguments) : string`; make `Run(...)` delegate to `RunAt(Root, ...)`.

- [ ] Run Infrastructure tests; expect the two `/var` versus `/private/var` failures.
- [ ] Move the existing `ProcessStartInfo` body into `RunAt`, using its `workingDirectory` parameter.
- [ ] For the main repo expected root use `Path.GetFullPath(repo.Run("rev-parse", "--show-toplevel").Trim())`.
- [ ] For the worktree use `Path.GetFullPath(repo.RunAt(worktreePath, "rev-parse", "--show-toplevel").Trim())`.
- [ ] Run `dotnet test tests/CodeKnowledge.Infrastructure.Tests --configuration Release`; expect PASS.
- [ ] Commit: `git commit -m "test: compare canonical git roots across platforms"`.

---

### Task 3: Native E2E publish target

**Files:**
- Create: `tests/CodeKnowledge.Mcp.Tests/PublishedServerTarget.cs`
- Create: `tests/CodeKnowledge.Mcp.Tests/PublishedServerTargetTests.cs`
- Modify: `tests/CodeKnowledge.Mcp.Tests/PublishedServerFixture.cs`

**Interfaces:** Produce `PublishedServerTarget(RuntimeIdentifier, ExecutableName)`, `ResolveCurrent()`, and testable `Resolve(OSPlatform, Architecture)`.

- [ ] Add tests mapping Windows/X64 to `win-x64` + `.exe`, OSX/Arm64 to `osx-arm64` + no extension, and rejecting Windows/Arm64, OSX/X64, Linux/X64 with `PlatformNotSupportedException`.
- [ ] Run MCP tests; expect build failure because the resolver is absent.
- [ ] Implement:

```csharp
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
```

- [ ] In the fixture resolve once, pass `target.RuntimeIdentifier` to publish, set `ExePath` from `target.ExecutableName`, and fail if it does not exist.
- [ ] Run MCP tests then `dotnet test CodeKnowledge.slnx --configuration Release`; expect zero failures/skips and no Mac `Exec format error`.
- [ ] Commit: `git commit -m "test: run published MCP server on Windows and Mac"`.

---

### Task 4: Dual-platform CI

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:** Matrix rows are `windows-2025/win-x64/CodeKnowledge.Mcp.exe` and `macos-15/osx-arm64/CodeKnowledge.Mcp`.

- [ ] Create a workflow triggered by main push, main PR, and `workflow_dispatch`, with `contents: read`.
- [ ] Use `actions/checkout@v6`, `actions/setup-dotnet@v5` with `10.0.201`, restore, and `dotnet test CodeKnowledge.slnx --configuration Release --no-restore`.
- [ ] Configure the matrix to run these exact platform publish commands:

```text
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --output artifacts/mcp/win-x64
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj --configuration Release --runtime osx-arm64 --self-contained false --no-restore --output artifacts/mcp/osx-arm64
```

- [ ] Add a `pwsh` check using `artifacts/mcp/${{ matrix.rid }}` and `${{ matrix.executable }}`; require the entry point and a file whose name matches `e_sqlite3`.
- [ ] Parse with `ruby -e 'require "yaml"; YAML.load_file(".github/workflows/ci.yml")'`; locally run all tests and `osx-arm64` publish; require executable entry point and native SQLite file.
- [ ] Commit: `git commit -m "ci: test Windows and Apple Silicon Mac"`.

---

### Task 5: Tag-driven release workflow

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:** Trigger `v*`; produce `CodeKnowledge-$GITHUB_REF_NAME-win-x64.zip`, `CodeKnowledge-$GITHUB_REF_NAME-osx-arm64.tar.gz`, and `SHA256SUMS`.

- [ ] Add `validate-tag` on Ubuntu. Accept `v1.2.3` and `v1.2.3-beta.1` with a shell regex; reject other `v*` tags.
- [ ] Add Windows and Mac jobs, each depending on validation, using the same SDK, full tests, and publish commands as CI.
- [ ] Windows packages the full publish directory using `Compress-Archive`.
- [ ] Mac runs:

```bash
chmod +x artifacts/mcp/osx-arm64/CodeKnowledge.Mcp
mkdir -p release
tar -C artifacts/mcp/osx-arm64 -czf \
  "release/CodeKnowledge-$GITHUB_REF_NAME-osx-arm64.tar.gz" .
verify="$(mktemp -d)"
tar -C "$verify" -xzf "release/CodeKnowledge-$GITHUB_REF_NAME-osx-arm64.tar.gz"
test -x "$verify/CodeKnowledge.Mcp"
```

- [ ] Upload archives with `actions/upload-artifact@v7`. Download both with `actions/download-artifact@v8`, `pattern: release-*`, and `merge-multiple: true`.
- [ ] In a final Ubuntu job with `contents: write`, generate and verify `SHA256SUMS`, refuse an existing release via `gh release view`, then run:

```bash
gh release create "$GITHUB_REF_NAME" \
  release/CodeKnowledge-$GITHUB_REF_NAME-win-x64.zip \
  release/CodeKnowledge-$GITHUB_REF_NAME-osx-arm64.tar.gz \
  release/SHA256SUMS --verify-tag --generate-notes \
  --title "CodeKnowledge $GITHUB_REF_NAME"
```

Set `GH_TOKEN` from `github.token`.
- [ ] Parse YAML and exercise Mac tar/create/extract/`test -x` under `/tmp`; do not push a tag.
- [ ] Commit: `git commit -m "ci: publish Windows and Mac releases"`.

---

### Task 6: Documentation and final verification

**Files:**
- Modify: `README.md`

**Interfaces:** Document exact targets and asset names from Tasks 4-5; retain dated Windows Phase 1 measurements as historical evidence.

- [ ] Replace the operational environment/build text with a two-row Windows 11 x64 and macOS 15+ arm64 table and both publish commands.
- [ ] Add Release selection, `SHA256SUMS`, framework-dependent Runtime requirement, unsupported platforms, and full-directory placement.
- [ ] Add Mac paths `$HOME/Tools/CodeKnowledge/CodeKnowledge.Mcp` and `$HOME/Tools/CodeKnowledge/knowledge.db`.
- [ ] Add trusted-release-only Gatekeeper instructions:

```bash
chmod +x CodeKnowledge.Mcp
xattr -d com.apple.quarantine CodeKnowledge.Mcp
```

- [ ] Add Mac Cursor/VS Code JSON and:

```bash
export CODEKNOWLEDGE_DB_PATH="$HOME/Tools/CodeKnowledge/data/knowledge.db"
claude mcp add --transport stdio --scope project code-knowledge -- \
  "$HOME/Tools/CodeKnowledge/CodeKnowledge.Mcp"
```

- [ ] Run `rg` for both RIDs, both entry points, checksum, unsigned warning, quarantine, and DB override; run `git diff --check`.
- [ ] Commit: `git commit -m "docs: add Windows and Mac distribution guide"`.
- [ ] Run clean full tests, final `osx-arm64` publish, `file` on the executable, SQLite-native-file check, both YAML parses, and `git diff --check`.
- [ ] After pushing through the selected integration flow, run `gh pr checks --watch`; require both OS jobs before claiming completion.
- [ ] Do not invent a release version. After the user supplies a tag, push it, watch the Release workflow, and verify exactly the Windows ZIP, Mac tar.gz, and `SHA256SUMS`.
