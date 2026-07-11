# Code Knowledge Phase 0 検証記録

このディレクトリは、Code Knowledge本実装の前提となるSQLite、MCP stdio、複数プロセス同時実行、およびWindows向け単一ファイル発行を検証するspikeである。Phase 0のコードを本番コードへ直接昇格させない。

現時点では自動検証だけが完了している。Cursor、GitHub Copilot in VS Code、Claude Codeによる実機検証とユーザー承認が未完了のため、**Phase 0は未完了であり、Phase 1へ移行できない**。

## 実行環境と技術前提

検証日: 2026-07-11

| 項目 | 実測値 |
|---|---|
| OS | Windows 10.0.26200 |
| RID | `win-x64` |
| .NET SDK | 10.0.201 |
| .NET Host Runtime | 10.0.5 (x64) |
| Target Framework | `net10.0` |
| EXE版 | 1.0.0.0 |
| SQLite版 | 3.50.4 |
| 発行方式 | framework-dependent、単一ファイル、`win-x64` |

使用した主要パッケージは次のとおり。

| パッケージ | バージョン |
|---|---:|
| Microsoft.Data.Sqlite | 10.0.9 |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.3 |
| Microsoft.Extensions.Hosting | 10.0.9 |
| ModelContextProtocol | 1.4.1 |
| Microsoft.NET.Test.Sdk | 18.7.0 |
| xunit.v3 | 3.2.2 |

## 自動検証手順

リポジトリルート `C:\zDev\repo\ck\.worktrees\phase0-probe` で、次のコマンドを順番に実行する。

```powershell
dotnet --info
dotnet restore CodeKnowledge.Phase0.slnx
dotnet test CodeKnowledge.Phase0.slnx --configuration Release
dotnet publish spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/phase0/win-x64
artifacts/phase0/win-x64/CodeKnowledge.Phase0.exe self-check
```

2026-07-11の実測結果:

| 検証 | 結果 | 実測 |
|---|---|---|
| restore | 成功 | 全プロジェクトが最新 |
| Release全テスト | 成功 | 不合格0、合格28、スキップ0、合計28 |
| `win-x64` publish | 成功 | 終了コード0 |
| 発行済みEXEのself-check | 成功 | 終了コード0、`Status = "ok"`、9項目すべて成功 |
| FTS5 trigram | 成功 | 仮想テーブル作成成功 |
| 日本語検索 | 成功 | 「メール」のMATCH、「仕様」「確認」のLIKE、混合検索が成功 |
| SQLite同時実行前提 | 成功 | `journal_mode = wal`、`busy_timeout = 5000`、`foreign_keys = 1` |

self-checkはstdoutへ単一JSONを出力する。主要な実測値は次のとおり。

```json
{
  "Mode": "self-check",
  "Status": "ok",
  "ExecutableVersion": "1.0.0.0",
  "Details": {
    "sqliteVersion": "3.50.4",
    "journalMode": "wal",
    "busyTimeout": "5000",
    "foreignKeys": "1"
  }
}
```

## MCPクライアント設定

発行済みEXEの絶対パスは次のとおり。

```text
C:\zDev\repo\ck\.worktrees\phase0-probe\artifacts\phase0\win-x64\CodeKnowledge.Phase0.exe
```

引数なしで起動するとMCP stdioサーバーとして動作し、`phase0_probe` Toolを1つ公開する。stdoutはMCP通信専用であり、通常ログには使用しない。

### Cursor

対象リポジトリの `.cursor/mcp.json`、またはユーザー全体の `~/.cursor/mcp.json` へ次を設定する。

```json
{
  "mcpServers": {
    "code-knowledge-phase0": {
      "type": "stdio",
      "command": "C:\\zDev\\repo\\ck\\.worktrees\\phase0-probe\\artifacts\\phase0\\win-x64\\CodeKnowledge.Phase0.exe",
      "args": [],
      "env": {}
    }
  }
}
```

設定後、Cursorから`phase0_probe`を明示的に呼び出し、`status = "ok"`、EXE版、SQLite版を確認する。

### GitHub Copilot in VS Code

対象リポジトリの `.vscode/mcp.json` へ次を設定する。トップレベルキーは`servers`を使用する。

```json
{
  "servers": {
    "code-knowledge-phase0": {
      "command": "C:\\zDev\\repo\\ck\\.worktrees\\phase0-probe\\artifacts\\phase0\\win-x64\\CodeKnowledge.Phase0.exe",
      "args": [],
      "env": {}
    }
  }
}
```

GitHub CopilotのAgentモードから`phase0_probe`を明示的に呼び出し、`status = "ok"`、EXE版、SQLite版を確認する。組織ポリシーでローカルMCPサーバーが許可されている必要がある。

### Claude Code

CLIからユーザー全体またはプロジェクト単位で登録する。

```powershell
# ユーザー全体
claude mcp add --transport stdio --scope user code-knowledge-phase0 -- "C:\zDev\repo\ck\.worktrees\phase0-probe\artifacts\phase0\win-x64\CodeKnowledge.Phase0.exe"

# プロジェクト単位
claude mcp add --transport stdio --scope project code-knowledge-phase0 -- "C:\zDev\repo\ck\.worktrees\phase0-probe\artifacts\phase0\win-x64\CodeKnowledge.Phase0.exe"
```

登録状態を確認する。

```powershell
claude mcp list
claude mcp get code-knowledge-phase0
```

Claude Codeから`phase0_probe`を明示的に呼び出し、`status = "ok"`、EXE版、SQLite版を確認する。プロジェクト単位の登録では、初回利用時に承認を求められる場合がある。

## 3クライアント実機検証結果

`未実施`は手動検証前の明示的な状態であり、成功を意味しない。各クライアントで実行した後、バージョン、検証日、Tool結果、設定上の所見を実測値で更新する。

| クライアント | バージョン | 検証日 | phase0_probe | EXE版 | SQLite版 | 設定・所見 |
|---|---|---|---|---|---|---|
| Cursor | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 |
| GitHub Copilot in VS Code | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 |
| Claude Code | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 |

## 発行成果物

`Get-ChildItem -File artifacts/phase0/win-x64 | Select-Object Name,Length` の2026-07-11実測結果:

| ファイル名 | サイズ (bytes) | 配布要否 |
|---|---:|---|
| `CodeKnowledge.Phase0.exe` | 6,646,292 | 必須 |

発行ディレクトリには上記EXEだけが存在し、隣接するネイティブSQLiteファイルは生成されなかった。framework-dependent発行のため、対象マシンには.NET 10 Runtimeが必要である。管理者権限やインストーラーを使わない配置での最終確認は、3クライアントの実機検証と同時に行う。

## Deviations

自動検証範囲ではなし。

3クライアントの実機検証は未実施であり、これは技術的前提との差異ではなく未完了の手動ゲートである。実機検証で要件定義書と異なる結果が出た場合は、前提、実測、再現手順、対応候補をこの節へ記録し、要件改訂と承認が完了するまでPhase 1への移行をブロックする。

## Phase 0完了判定

- [x] Release構成の全自動テスト成功
- [x] 発行済みEXEのself-check成功
- [ ] Cursorでphase0_probe成功
- [ ] GitHub Copilot in VS Codeでphase0_probe成功
- [ ] Claude Codeでphase0_probe成功
- [x] 発行成果物一覧を記録
- [x] Deviationsが「なし」、または要件改訂が承認済み
- [ ] ユーザーがPhase 0完了とPhase 1移行を承認

3クライアントの結果が揃ったらこのREADMEを更新し、`docs: record phase 0 client verification`として自動検証記録とは別にコミットする。全完了条件とユーザー承認が揃うまで、Phase 1の設計・実装を開始しない。
