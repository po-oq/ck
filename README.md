# Code Knowledge

Code Knowledgeは、コード調査の結果をGitプロジェクト単位でSQLiteへ保存し、別の調査や別セッションから再利用するためのMCP stdioサーバーである。

Phase 2では、最新の確定ナレッジを保存・検索・取得・検証する5 Toolを提供する。すべてのToolは`workingDirectory`から対象Gitリポジトリを解決し、別プロジェクトのナレッジが混ざらないようにする。

| Tool | 用途 |
|---|---|
| `resolve_project` | 現在のGitリポジトリをCode Knowledge上のプロジェクトへ解決する |
| `search_knowledge` | FTS5と部分一致を組み合わせ、現在のプロジェクトに保存されたナレッジを検索する |
| `get_knowledge` | ナレッジの本文、事実、推論、根拠、関連を取得する |
| `save_knowledge` | ユーザーの明示指示または保存提案への同意後に、調査結果を保存する |
| `validate_knowledge` | 最新確定ナレッジをHEADまたは指定コミットに対して検証し、根拠ごとの鮮度と未コミット変更を返す |

Phase 3以降で追加予定の差分比較、プロジェクト移行などのToolは、Phase 2には含まれない。

## 動作環境

| OS | CPU | RID | 実行ファイル | 必要なRuntime |
|---|---|---|---|---|
| Windows 11 | x64 | `win-x64` | `CodeKnowledge.Mcp.exe` | .NET 10 Runtime x64 |
| macOS 15以降 | Apple Silicon arm64 | `osx-arm64` | `CodeKnowledge.Mcp` | .NET 10 Runtime Arm64 |

両OSともGit CLIが必要である。発行方式はframework-dependentの単一ファイルであり、対象CPU用の.NET 10 Runtimeを利用者側へインストールする。self-contained配布ではない。管理者権限やインストーラーは不要だが、SQLiteのネイティブライブラリなども必要になるため、実行ファイルだけを取り出さず、発行ディレクトリまたはReleaseアーカイブの内容一式を同じローカルフォルダへ配置する。

Linux、Intel Mac、Windows Arm64は未対応である。Mac版はDeveloper ID署名およびApple公証を行っていない。

## GitHub Releaseから導入

GitHub Releasesから使用するタグのReleaseを開き、用途とOSに対応するアーカイブと`SHA256SUMS`をダウンロードする。ファイル名の`<tag>`部分には、そのReleaseのタグ名が入る。各Releaseには**MCPサーバー**と**CLI**の両方が含まれ、`CodeKnowledge-Cli-` 接頭辞が付くものがCLIである。MCPとCLIは独立して導入でき、どちらか一方だけを使ってもよい。

| 用途 | 対象 | ダウンロードするアーカイブ |
|---|---|---|
| MCPサーバー | Windows 11 x64 | `CodeKnowledge-<tag>-win-x64.zip` |
| MCPサーバー | macOS 15以降 Apple Silicon | `CodeKnowledge-<tag>-osx-arm64.tar.gz` |
| CLI | Windows 11 x64 | `CodeKnowledge-Cli-<tag>-win-x64.zip` |
| CLI | macOS 15以降 Apple Silicon | `CodeKnowledge-Cli-<tag>-osx-arm64.tar.gz` |

以降の検証・展開手順はMCPサーバーのアーカイブを例に説明する。CLIを導入する場合は、ファイル名の`CodeKnowledge-`を`CodeKnowledge-Cli-`に、実行ファイル名の`CodeKnowledge.Mcp`を`CodeKnowledge.Cli`に読み替える。

`SHA256SUMS`に記載された対象アーカイブのSHA-256と、ダウンロードしたファイルのSHA-256が一致することを確認してから展開する。Windowsでは次の出力を`SHA256SUMS`の該当行と比較する。

```powershell
Get-FileHash "CodeKnowledge-<tag>-win-x64.zip" -Algorithm SHA256
```

Macでは`<Releaseのタグ名>`を実際のタグ名に置き換え、対象アーカイブの行だけを検証する。

```bash
TAG='<Releaseのタグ名>'
grep "CodeKnowledge-${TAG}-osx-arm64.tar.gz" SHA256SUMS | \
  shasum -a 256 -c -
```

WindowsではZIPの内容一式を、たとえば`C:\Tools\CodeKnowledge`へ展開する。Macでは次のように、tar.gzの内容一式を同期対象外の安定したローカルフォルダへ展開する。

```bash
TAG='<Releaseのタグ名>'
mkdir -p "$HOME/Tools/CodeKnowledge"
tar -xzf "CodeKnowledge-${TAG}-osx-arm64.tar.gz" \
  -C "$HOME/Tools/CodeKnowledge"
```

Mac版は未署名・未公証のため、Gatekeeperにより起動を拒否される場合がある。次の操作は、チェックサムを確認し、自分が信頼した公式GitHub Releaseから取得したファイルに限って実行する。第三者から入手したファイルではquarantineを解除しない。

```bash
cd "$HOME/Tools/CodeKnowledge"
chmod +x CodeKnowledge.Mcp
xattr -d com.apple.quarantine CodeKnowledge.Mcp
```

## ビルドと発行

リポジトリルートでテスト後、対象OSのRIDを指定して発行する。

```bash
dotnet test CodeKnowledge.slnx --configuration Release

# Windows 11 x64
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/mcp/win-x64

# macOS 15以降 Apple Silicon
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj --configuration Release --runtime osx-arm64 --self-contained false --output artifacts/mcp/osx-arm64
```

発行後のエントリーポイントは次のファイルである。配布時は、対応する`artifacts/mcp/<RID>`ディレクトリの内容一式を使用する。

```text
artifacts\mcp\win-x64\CodeKnowledge.Mcp.exe
artifacts/mcp/osx-arm64/CodeKnowledge.Mcp
```

クライアントへ登録する前に、発行ディレクトリ一式を同期対象外の安定したローカルフォルダへ配置する。以降の設定例では次の絶対パスを使用する。

```text
C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe
$HOME/Tools/CodeKnowledge/CodeKnowledge.Mcp
```

### CLIの発行（Windows 11 x64 / macOS Apple Silicon）

CLIはMCPと同じCore/Infrastructureを使うクロスプラットフォームな.NETアプリであり、Windowsに加えてApple Silicon Macでも動作する。対象OSのRIDを指定して発行する。

```bash
# Windows 11 x64
dotnet publish src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj \
  --configuration Release --runtime win-x64 --self-contained false \
  --output artifacts/cli/win-x64

# macOS 15以降 Apple Silicon
dotnet publish src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj \
  --configuration Release --runtime osx-arm64 --self-contained false \
  --output artifacts/cli/osx-arm64
```

発行後のエントリーポイントは次のファイルである。配布時は、対応する`artifacts/cli/<RID>`ディレクトリの内容一式を使用する。

```text
artifacts\cli\win-x64\CodeKnowledge.Cli.exe
artifacts/cli/osx-arm64/CodeKnowledge.Cli
```

利用者側には対応する.NET 10 Runtime（x64 / Arm64）が必要である。Linux、Intel Mac、Windows Arm64は対象外である。

Mac版は未署名・未公証のため、Gatekeeperにより起動を拒否される場合がある。次の操作は、チェックサムを確認し、自分が信頼した公式GitHub Releaseから取得したファイルに限って実行する。第三者から入手したファイルではquarantineを解除しない。

```bash
cd "$HOME/Tools/CodeKnowledge"
chmod +x CodeKnowledge.Cli
xattr -d com.apple.quarantine CodeKnowledge.Cli
```

CLIとMCPで同じナレッジを共有するには、両方に同じ `CODEKNOWLEDGE_DB_PATH` を設定する。
未設定の場合、DBは各実行ファイル隣の `knowledge.db` になり、MCPとCLIで別々になる。

### CLIをAgentに使わせる設定

`docs/agent-rules/code-knowledge-cli.md` の内容を、利用先リポジトリの次のファイルへ転記する
（実体は3種。Copilotの2環境は同じファイルを読む）。

| 環境 | ルールファイル |
|---|---|
| Cursor | `.cursor/rules/code-knowledge.mdc` |
| Claude Code | `CLAUDE.md` |
| GitHub Copilot（VS Code / Visual Studio） | `.github/copilot-instructions.md` |

CLI実行ファイルの絶対パスは各環境の配置に合わせて置き換える（Windowsは`...\CodeKnowledge.Cli.exe`、Macは`.../CodeKnowledge.Cli`）。

## 配置とデータベース

### 既定の配置

データベースは既定で実行ファイルと同じフォルダの`knowledge.db`に作成される。

```text
C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe
C:\Tools\CodeKnowledge\knowledge.db

$HOME/Tools/CodeKnowledge/CodeKnowledge.Mcp
$HOME/Tools/CodeKnowledge/knowledge.db
```

起動時に必要なスキーマへ自動マイグレーションされる。SQLite接続ではWAL、5秒のbusy timeout、外部キー制約が有効になる。

### データベースパスの上書き

環境変数`CODEKNOWLEDGE_DB_PATH`を設定すると、既定のDBパスを上書きできる。複数のMCPクライアントで同じナレッジを共有する場合は、全クライアントへ同じ値を設定する。

```powershell
$env:CODEKNOWLEDGE_DB_PATH = 'C:\Tools\CodeKnowledge\data\knowledge.db'
```

Macのシェルで設定する場合は次のとおり。

```bash
export CODEKNOWLEDGE_DB_PATH="$HOME/Tools/CodeKnowledge/data/knowledge.db"
```

クライアント設定の`env`へ指定する場合は、WindowsパスのバックスラッシュをJSON用にエスケープする。

```json
{
  "env": {
    "CODEKNOWLEDGE_DB_PATH": "C:\\Tools\\CodeKnowledge\\data\\knowledge.db"
  }
}
```

### 更新と移動

- バージョン更新時はMCPクライアントを停止し、同じReleaseアーカイブまたは発行結果に含まれるファイル一式でプログラムファイルを更新する。既存の`knowledge.db`と付随ファイルは削除しない。
- 配置フォルダを移動する場合は、MCPクライアントを停止し、実行ファイル、同梱ライブラリ、`knowledge.db`、`knowledge.db-wal`、`knowledge.db-shm`を含むフォルダ全体を一緒に移動する。
- OneDrive、Dropboxなどの同期フォルダやネットワーク共有には配置しない。ファイル同期とSQLite WALが競合し、DBが破損する可能性がある。
- DBファイルだけをコピーして稼働中のDBをバックアップしない。WAL内の未チェックポイントデータが欠落する可能性がある。

## MCPクライアント設定

`command`には対象OSの実行ファイルの絶対パスを指定する。サーバーのstdoutはMCP通信専用であり、起動通知とログはstderrへ出力される。

### Cursor

対象リポジトリの`.cursor/mcp.json`、またはユーザー全体の`~/.cursor/mcp.json`へ次を設定する。

```json
{
  "mcpServers": {
    "code-knowledge": {
      "type": "stdio",
      "command": "C:\\Tools\\CodeKnowledge\\CodeKnowledge.Mcp.exe",
      "args": [],
      "env": {}
    }
  }
}
```

Macでは`command`を次のように指定する。

```json
{
  "mcpServers": {
    "code-knowledge": {
      "type": "stdio",
      "command": "/Users/<user>/Tools/CodeKnowledge/CodeKnowledge.Mcp",
      "args": [],
      "env": {
        "CODEKNOWLEDGE_DB_PATH": "/Users/<user>/Tools/CodeKnowledge/data/knowledge.db"
      }
    }
  }
}
```

JSON内では`$HOME`や`~`が展開されないため、`<user>`を実際のユーザー名に置き換えた絶対パスを指定する。

設定後、CursorのAgentから`resolve_project`を呼び出し、現在のGitリポジトリが解決されることを確認する。プロジェクト単位の設定ファイルをリポジトリへ含めるかは、組織のポリシーに従う。

### GitHub Copilot in VS Code

対象リポジトリの`.vscode/mcp.json`へ次を設定する。トップレベルキーは`servers`を使用する。

```json
{
  "servers": {
    "code-knowledge": {
      "command": "C:\\Tools\\CodeKnowledge\\CodeKnowledge.Mcp.exe",
      "args": [],
      "env": {}
    }
  }
}
```

Macでは次のように絶対パスを指定する。

```json
{
  "servers": {
    "code-knowledge": {
      "command": "/Users/<user>/Tools/CodeKnowledge/CodeKnowledge.Mcp",
      "args": [],
      "env": {
        "CODEKNOWLEDGE_DB_PATH": "/Users/<user>/Tools/CodeKnowledge/data/knowledge.db"
      }
    }
  }
}
```

設定後、GitHub CopilotのAgentモードから`resolve_project`を呼び出す。EnterpriseまたはOrganizationのMCPポリシーでローカルMCPサーバーが許可されている必要がある。

### Claude Code

プロジェクト単位で登録する。

```powershell
claude mcp add --transport stdio --scope project code-knowledge -- "C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe"
```

MacでDBパスを上書きして登録する場合は、`--env`でプロジェクト設定へ保存する。現在のシェルだけに設定する`export`は不要であり、登録後に開始したClaude CodeセッションでもこのDBパスが使用される。

```bash
claude mcp add --transport stdio --scope project \
  --env "CODEKNOWLEDGE_DB_PATH=$HOME/Tools/CodeKnowledge/data/knowledge.db" \
  code-knowledge -- \
  "$HOME/Tools/CodeKnowledge/CodeKnowledge.Mcp"
```

登録状態を確認する。

```powershell
claude mcp list
claude mcp get code-knowledge
```

プロジェクト単位の登録では、初回利用時に承認を求められる場合がある。承認後、新しいClaude Codeセッションから5 Toolの実機検証を行う。

## ナレッジの鮮度検証

`search_knowledge`で関連ナレッジを見つけた後、保存内容を回答へ再利用する前に`validate_knowledge`を呼ぶ。`targetCommit`を省略すると現在のHEADを検証し、指定するとローカルで解決できるそのcommit-ishを検証する。空白だけの`targetCommit`は`invalid_arguments`になる。

入力例:

```json
{
  "workingDirectory": "C:\\work\\order-system",
  "knowledgeId": "knowledge-001",
  "targetCommit": "HEAD"
}
```

出力例:

```json
{
  "status": "partially_stale",
  "baseCommit": "abc1234567890abc1234567890abc1234567890a",
  "targetCommit": "def4567890abcdef4567890abcdef4567890abcd",
  "isWorkingTreeDirty": true,
  "changedEvidence": ["SmtpEmailSender.SendAsync"],
  "unchangedEvidence": ["OrderService.CompleteAsync"],
  "missingEvidence": [],
  "unknownEvidence": [],
  "dirtyEvidence": ["SmtpEmailSender.SendAsync"],
  "evidenceDetails": [
    {
      "evidenceId": "evidence-001",
      "label": "SmtpEmailSender.SendAsync",
      "originalFilePath": "src/Mail/SmtpEmailSender.cs",
      "targetFilePath": "src/Mail/SmtpEmailSender.cs",
      "status": "changed",
      "reason": "symbol_hash_not_found",
      "isWorkingTreeDirty": true
    }
  ],
  "recommendedAction": "reinspect_changed_symbols",
  "warnings": []
}
```

全体の`status`は次の意味を持つ。

| status | 意味 |
|---|---|
| `valid` | 全Evidenceが再利用可能 |
| `partially_stale` | 再利用可能なEvidenceと変更・削除されたEvidenceが混在 |
| `stale` | 全Evidenceが変更または削除済み |
| `unknown` | Evidence 0件、判定不能Evidenceあり、またはdirty確認不能 |

`evidenceDetails[].status`は次の意味を持つ。

| status | 意味 |
|---|---|
| `unchanged` | 保存時と同じファイルまたはシンボル範囲ハッシュを確認できた |
| `changed` | ファイルは存在するが保存済みシンボル範囲ハッシュを再特定できない |
| `missing` | rename対応後の対象ファイルが存在しない |
| `unknown` | 必要なcommit、内容、diff、またはsymbol hashを確定できない |

`recommendedAction`は次の行動を示す。

| recommendedAction | 行動 |
|---|---|
| `reuse_knowledge` | 保存済みナレッジを主に利用する |
| `inspect_dirty_evidence` | dirtyなEvidenceファイルを直接確認する |
| `reinspect_changed_symbols` | changed、missing、dirtyなEvidenceを再確認する |
| `reinvestigate_knowledge` | ナレッジ全体を再調査する |
| `inspect_evidence` | unknown、warning、dirtyなEvidenceを直接確認する |

`isWorkingTreeDirty = true`なら、コミット間の鮮度が`valid`でも`dirtyEvidence`のファイルを直接確認する。`isWorkingTreeDirty = null`はdirty検出失敗を表し、全体の`status`は`unknown`になる。`unknownEvidence`、`warnings`、`evidenceDetails[].reason`を使って確認対象と理由を特定する。

検証は新しいナレッジバージョンや検証結果を保存せず、ナレッジを自動更新しない。Phase 2はGit renameと、対応付けたファイル内で正規化後のハッシュが一致するシンボル範囲移動を追跡する。Roslynによる構文解析、意味的なシンボル名変更の追跡、類似ファイルのリポジトリ全体探索は行わない。

## Agent行動ルール

次のブロックを`AGENTS.md`、`CLAUDE.md`など、利用するAgentの指示ファイルへコピーする。

Phase 2で有効なルールは1〜9、12、13である。10、11はPhase 3以降のToolに依存するため、全文を将来用に記載しているが、Phase 2では実行しない。

```markdown
## Code Knowledge MCP利用ルール

既存コードの仕様、構造、処理フロー、過去の調査内容について質問された場合、
コード全体を調査する前にCode Knowledge MCPを検索すること。

Phase 2で有効なルールは1〜9、12、13である。
「Phase 3以降」と記載されたルールは、必要なToolが提供されるまで実行しないこと。

手順:

1. 現在のプロジェクトを`resolve_project`で解決する。
2. `search_knowledge`で関連ナレッジを検索する。
   keywordsは網羅的に展開する: 質問中の名詞・複合語、英語表記、
   推測されるシンボル名を含め、3文字以上の複合語を優先する。
   2文字の重要語も含めてよい（サーバー側で部分一致検索される）。
3. 関連ナレッジが存在する場合は`validate_knowledge`を実行する。
4. `valid`の場合、保存済みナレッジを主に利用し、必要最小限のコード確認のみ行う。
   ただし`isWorkingTreeDirty`がtrueの場合、`dirtyEvidence`のファイルは直接確認する。
5. `partially_stale`の場合、`changedEvidence`、`missingEvidence`、`dirtyEvidence`の
   該当箇所と関連箇所だけを直接確認する。
6. `stale`の場合、ナレッジ全体を再調査し、`dirtyEvidence`も直接確認する。
7. `unknown`の場合、`unknownEvidence`、`warnings`、`evidenceDetails[].reason`から
   判定不能理由を確認し、該当コードと`dirtyEvidence`を直接確認する。
8. 保存済みナレッジなしで新規のコード調査を完了した場合、調査結果を
   ナレッジとして保存するかユーザーへ提案する。ユーザーの明示指示または
   提案への同意がある場合のみ`save_knowledge`を呼び出す。
   同意なしに自動保存しない。
9. 新しい調査結果を保存する前に、既存の類似ナレッジを検索で確認する。
   保存時は事実と推論を分離し、事実には必ず根拠を付ける。
10. [Phase 3以降] 差分調査結果は、ユーザーが保存を指示した場合のみ永続保存する。
    保存には`compare_knowledge`が返した`temporaryComparisonId`を指定する。
11. [Phase 3以降] `resolve_project`が`project_id_changed`警告を返した場合、ユーザーへ通知し、
    ユーザーの明示指示がある場合のみ`migrate_project`を実行する。
12. 確信度が`low`の推論を回答の根拠にする場合、該当コードを直接確認してから使用する。
13. 別プロジェクトのナレッジを検索または回答に使用しない。
```

## 検証記録

以下はクロスプラットフォーム対応前に完了したPhase 1のWindows実測記録であり、日付と測定値を履歴として保持している。Mac対応や現在のCI結果を示す記録ではない。

2026-07-13に自動テスト、最終publish、Claude Codeからの実機検証を実施した。CursorとGitHub Copilot in VS Codeの実機検証は、2026-07-11のユーザー判断により対象外としている。

### 実行環境

検証日: 2026-07-13

| 項目 | 実測値 |
|---|---|
| OS | Windows 10.0.26200 |
| RID | `win-x64` |
| .NET SDK | 10.0.201 |
| .NET Host Runtime | 10.0.5 (x64) |
| Target Framework | `net10.0` |
| 発行方式 | framework-dependent、単一ファイル、`win-x64` |

### 自動検証結果

| 検証 | 状態 | 実測値 |
|---|---|---|
| Release全テスト | 成功 | 不合格0、合格128、スキップ0（Core 81 / Infrastructure 39 / Mcp 8） |
| 発行済みEXEのMCP E2E | 成功 | Mcp 8件のうち発行済みEXEを使うE2E 5件が成功 |
| `win-x64`最終publish | 成功 | 終了コード0。worktreeの発行EXEと実機検証に使用したEXEのSHA-256が一致 |

### MCPクライアント実機検証

| クライアント | バージョン | 検証日 | `resolve_project` | `save_knowledge` | `search_knowledge` | `get_knowledge` | 設定・所見 |
|---|---|---|---|---|---|---|---|
| Cursor | 対象外 | 2026-07-11 | 対象外 | 対象外 | 対象外 | 対象外 | 実施環境がないため、ユーザー判断により検証対象外。設定手順のみ整備 |
| GitHub Copilot in VS Code | 対象外 | 2026-07-11 | 対象外 | 対象外 | 対象外 | 対象外 | 実施環境がないため、ユーザー判断により検証対象外。設定手順のみ整備 |
| Claude Code | 2.1.162 | 2026-07-13 | 成功 | 成功 | 成功 | 成功 | `projectId = github.com/po-oq/ck`、警告・エラーなし。保存した`knowledgeId = 019f58551e3c757eaa4863a7c0e864fc`がFTS検索で一致し、facts 7件・evidence 4件を取得 |

### 発行成果物

次のコマンドによる最終publish直後の実測結果:

```powershell
Get-ChildItem -File artifacts/mcp/win-x64 | Select-Object Name,Length
```

| ファイル名 | サイズ (bytes) | 配布要否 |
|---|---:|---|
| `CodeKnowledge.Mcp.exe` | 4,838,487 | 必須 |
| `e_sqlite3.dll` | 1,911,296 | 必須 |
| `CodeKnowledge.Core.pdb` | 30,008 | 任意（デバッグ用） |
| `CodeKnowledge.Infrastructure.pdb` | 17,936 | 任意（デバッグ用） |
| `CodeKnowledge.Mcp.pdb` | 15,980 | 任意（デバッグ用） |

実機検証には`C:\zDev\repo\ck\artifacts\mcp\win-x64\CodeKnowledge.Mcp.exe`を使用した。このEXEのSHA-256はworktreeで最終publishしたEXEと一致している。既定DBパスとして同じディレクトリへ`knowledge.db`、`knowledge.db-wal`、`knowledge.db-shm`が生成されることを確認した。DBファイルは実行時データであり、発行成果物には含めない。

## 要件定義書からの変更点（Deviations）

| # | 変更 | 理由 | 決定 |
|---|---|---|---|
| 1 | DB既定パスを`%LOCALAPPDATA%\CodeKnowledge\knowledge.db`からEXE隣接の`knowledge.db`へ変更し、`CODEKNOWLEDGE_DB_PATH`で上書き可能とした | DB位置を自明にし、ポータブルに運用するため | 2026-07-11 ユーザー承認 |
| 2 | `knowledge_versions.tags`を追加した | 要件8.2の検索対象「タグ」と要件6.2のモデル定義の不整合を解消するため | Phase 1設計で確定、要件へフィードバック |
| 3 | 実機検証をClaude Codeのみとし、CursorとGitHub Copilot in VS Codeを検証対象外とした | Cursor / Copilotの実施環境がないため | 2026-07-11 ユーザー承認 |
| 4 | Toolエラーを構造化JSON`{code, message}`ではなく、`McpException`経由で`content[0].text`内の`"{code}: {message}"`として返す。クライアントは`"<code>: "`を部分文字列として照合する | ModelContextProtocol SDKが可変プレフィックスを付けてエラーを表面化するため | 2026-07-12 レビューで確定 |

## Phase 1完了ゲート

- [x] Release構成の全自動テスト成功
- [x] 発行済み`CodeKnowledge.Mcp.exe`のE2Eテスト成功
- [x] `win-x64`最終publish成功と発行成果物一覧の記録
- [x] Claude Codeから4 Toolを実機で呼び出し、「保存 → 検索 → 取得」が実リポジトリで成功
- [x] Cursor / GitHub Copilot in VS Codeの実機検証免除をDeviationsへ記録
- [x] Agent行動ルールと3クライアントの設定手順を記録
- [x] ユーザーがPhase 1完了を承認（2026-07-13）

2026-07-13、すべての完了条件とユーザー承認が揃い、Phase 1は完了した。Phase 2は別途計画・承認して開始する。
