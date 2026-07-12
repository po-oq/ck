# Code Knowledge

Code Knowledgeは、コード調査の結果をGitプロジェクト単位でSQLiteへ保存し、別の調査や別セッションから再利用するためのMCP stdioサーバーである。

Phase 1では、最新の確定ナレッジを保存・検索・取得する最小実用版として、次の4 Toolを提供する。すべてのToolは`workingDirectory`から対象Gitリポジトリを解決し、別プロジェクトのナレッジが混ざらないようにする。

| Tool | 用途 |
|---|---|
| `resolve_project` | 現在のGitリポジトリをCode Knowledge上のプロジェクトへ解決する |
| `search_knowledge` | FTS5と部分一致を組み合わせ、現在のプロジェクトに保存されたナレッジを検索する |
| `get_knowledge` | ナレッジの本文、事実、推論、根拠、関連を取得する |
| `save_knowledge` | ユーザーの明示指示または保存提案への同意後に、調査結果を保存する |

Phase 2以降で追加予定の鮮度検証、差分比較、プロジェクト移行などのToolは、Phase 1には含まれない。

## 動作環境

- Windows x64
- .NET 10 Runtime x64
- Git CLI

発行方式はframework-dependentの単一ファイルである。マネージド依存関係はEXEへ束ねられるが、ネイティブSQLiteライブラリ`e_sqlite3.dll`は別ファイルとして発行される。対象マシンに.NET 10 Runtimeが必要だが、管理者権限やインストーラーは不要で、発行ディレクトリ一式を任意のローカルフォルダへ配置して実行できる。

## ビルドと発行

リポジトリルートで次のコマンドを実行する。

```powershell
dotnet test CodeKnowledge.slnx --configuration Release
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/mcp/win-x64
```

発行後のエントリーポイントは次のファイルである。

```text
artifacts\mcp\win-x64\CodeKnowledge.Mcp.exe
```

クライアントへ登録する前に、発行ディレクトリ一式を同期対象外の安定したローカルフォルダへ配置する。`CodeKnowledge.Mcp.exe`と`e_sqlite3.dll`は必ず同じフォルダに置く。以降の設定例では次の絶対パスを使用する。

```text
C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe
C:\Tools\CodeKnowledge\e_sqlite3.dll
```

## 配置とデータベース

### 既定の配置

データベースは既定でEXEと同じフォルダの`knowledge.db`に作成される。

```text
C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe
C:\Tools\CodeKnowledge\knowledge.db
```

起動時に必要なスキーマへ自動マイグレーションされる。SQLite接続ではWAL、5秒のbusy timeout、外部キー制約が有効になる。

### データベースパスの上書き

環境変数`CODEKNOWLEDGE_DB_PATH`を設定すると、既定のDBパスを上書きできる。複数のMCPクライアントで同じナレッジを共有する場合は、全クライアントへ同じ値を設定する。

```powershell
$env:CODEKNOWLEDGE_DB_PATH = 'C:\Tools\CodeKnowledge\data\knowledge.db'
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

- バージョン更新時はMCPクライアントを停止してから、`CodeKnowledge.Mcp.exe`と`e_sqlite3.dll`を同じ発行結果の組み合わせで上書きする。
- 配置フォルダを移動する場合は、MCPクライアントを停止し、`CodeKnowledge.Mcp.exe`、`e_sqlite3.dll`、`knowledge.db`、`knowledge.db-wal`、`knowledge.db-shm`を一緒に移動する。PDBも保持する場合は同じフォルダごと移動する。
- OneDrive、Dropboxなどの同期フォルダやネットワーク共有には配置しない。ファイル同期とSQLite WALが競合し、DBが破損する可能性がある。
- DBファイルだけをコピーして稼働中のDBをバックアップしない。WAL内の未チェックポイントデータが欠落する可能性がある。

## MCPクライアント設定

`command`には`CodeKnowledge.Mcp.exe`の絶対パスを指定する。サーバーのstdoutはMCP通信専用であり、起動通知とログはstderrへ出力される。

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

設定後、GitHub CopilotのAgentモードから`resolve_project`を呼び出す。EnterpriseまたはOrganizationのMCPポリシーでローカルMCPサーバーが許可されている必要がある。

### Claude Code

プロジェクト単位で登録する。

```powershell
claude mcp add --transport stdio --scope project code-knowledge -- "C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe"
```

登録状態を確認する。

```powershell
claude mcp list
claude mcp get code-knowledge
```

プロジェクト単位の登録では、初回利用時に承認を求められる場合がある。承認後、新しいClaude Codeセッションから4 Toolの実機検証を行う。

## Agent行動ルール

次のブロックを`AGENTS.md`、`CLAUDE.md`など、利用するAgentの指示ファイルへコピーする。

Phase 1で有効なルールは1、2、8、9、13である。3〜7と10〜12はPhase 2以降のToolに依存するため、全文を将来用に記載しているが、Phase 1では実行しない。

```markdown
## Code Knowledge MCP利用ルール

既存コードの仕様、構造、処理フロー、過去の調査内容について質問された場合、
コード全体を調査する前にCode Knowledge MCPを検索すること。

Phase 1で有効なルールは1、2、8、9、13である。
「Phase 2以降」と記載されたルールは、必要なToolが提供されるまで実行しないこと。

手順:

1. 現在のプロジェクトを`resolve_project`で解決する。
2. `search_knowledge`で関連ナレッジを検索する。
   keywordsは網羅的に展開する: 質問中の名詞・複合語、英語表記、
   推測されるシンボル名を含め、3文字以上の複合語を優先する。
   2文字の重要語も含めてよい（サーバー側で部分一致検索される）。
3. [Phase 2以降] 関連ナレッジが存在する場合は`validate_knowledge`を実行する。
4. [Phase 2以降] `valid`の場合、保存済みナレッジを主に利用し、必要最小限のコード確認のみ行う。
   ただし`isWorkingTreeDirty`がtrueの場合、該当ファイルは直接確認する。
5. [Phase 2以降] `partially_stale`の場合、変更された根拠および関連箇所のみ再調査する。
6. [Phase 2以降] `stale`の場合、通常のコード調査を行う。
7. [Phase 2以降] `unknown`の場合、根拠コードを確認して判断する。
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
12. [Phase 2以降] 確信度が`low`の推論を回答の根拠にする場合、該当コードを直接確認してから使用する。
13. 別プロジェクトのナレッジを検索または回答に使用しない。
```

## 検証記録

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
