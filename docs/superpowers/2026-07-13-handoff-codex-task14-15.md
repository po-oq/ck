# 引き継ぎ資料: Code Knowledge Phase 1 — Task 14〜15（Codex向け）

作成日: 2026-07-13 / 作成者: Claude Code（Fable 5 → Opus 4.8）
対象: このブランチの続きを実装する次のエージェント（Codex）

---

## 0. 30秒サマリー

- Code Knowledge Phase 1 の実装計画（全15タスク）を **Task 1〜13 まで完了**。残りは **Task 14（README作成）と Task 15（Phase 1完了ゲート）** のみ。
- コードは**すべて完成・テスト緑**。Task 14 は純粋なドキュメント作成、Task 15 は最終検証＋**ユーザー承認待ち**（承認はCodexが勝手に代行しない）。
- 作業ブランチは `worktree-code-knowledge-phase1`（git worktree 内、`main` 未マージ）。
- テスト: **128/128 成功**（Core 81 / Infrastructure 39 / Mcp 8）、`dotnet build` 警告0。

---

## 1. 作業場所（重要）

このブランチは **git worktree** 上にある。ファイルはすべてこの worktree ディレクトリで編集・コミットすること。

| 項目 | 値 |
|---|---|
| worktree パス | `C:\zDev\repo\ck\.claude\worktrees\code-knowledge-phase1` |
| ブランチ | `worktree-code-knowledge-phase1` |
| 分岐元 | `main` の `e9e4b62`（`docs: add phase 1 implementation plan`） |
| 現在の HEAD | `5828614`（`docs: check off phase 1 plan task 13`） |
| main へのマージ | **未実施**（Phase 1完了承認後に判断） |

メインのチェックアウト（`C:\zDev\repo\ck`）ではなく、上記 worktree で作業する。過去に実装エージェントがメインリポジトリ側へ誤ってファイルを置いた事故があったので注意。

---

## 2. 環境とコマンド

- OS: Windows 11 / シェルは PowerShell（bash も利用可、それぞれ構文が違う）
- .NET SDK: **10.0.201**、ターゲット `net10.0`、C#。中央パッケージ管理（`Directory.Packages.props`）。`Directory.Build.props` で `Nullable=enable` / `ImplicitUsings=enable` / **`TreatWarningsAsErrors=true`**。
- ソリューション: `CodeKnowledge.slnx`（src 3 + tests 3）。別に `CodeKnowledge.Phase0.slnx` と `spikes/` があるが**触らない・参照しない**。

```powershell
# テスト（E2E含むMcp.Testsは発行を伴い1〜2分かかる）
dotnet test CodeKnowledge.slnx --configuration Release

# 発行（Task 14/15 のREADMEに載せる／Task 15で実行する正式コマンド）
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/mcp/win-x64
```

`artifacts/` は `.gitignore` 済み（コミットされない）。

---

## 3. アーキテクチャ早見

- 3プロジェクト構成: `CodeKnowledge.Core`（Domain + Application、**外部依存なし=BCLのみ**）、`CodeKnowledge.Infrastructure`（SQLite / Git CLI / ハッシュ）、`CodeKnowledge.Mcp`（MCP stdio アダプター、EXE）。依存方向は `Mcp → Core ← Infrastructure`。
- MCP Tool は4つ: `resolve_project` / `search_knowledge` / `get_knowledge` / `save_knowledge`。全ツールが `workingDirectory` を受け取り、Agent供給の projectId は信用しない（要件16章）。
- DB は既定で EXE 隣接の `knowledge.db`（環境変数 `CODEKNOWLEDGE_DB_PATH` で上書き）。起動時に `PRAGMA user_version` で v1 マイグレーション（10テーブル: FTS5 trigram 含む）。
- stdout は MCP プロトコル専用、ログは stderr のみ。

---

## 4. 残タスクの詳細

計画書の該当箇所: `docs/superpowers/plans/2026-07-11-code-knowledge-phase1.md` の **Task 14 = L4072-4106、Task 15 = L4108-4146**。以下は要点。**計画本文を必ず読むこと。**

### Task 14: README・クライアント設定・Agent行動ルール（純ドキュメント）

- 作成: `README.md`（**worktree のルート**）。
- 章立て（値はすべて実測・実物に合わせる）:
  1. 概要（Code Knowledgeとは／Phase 1の4 Tool）
  2. ビルドと発行（§2のコマンド）
  3. 配置とDBルール（EXE隣接 `knowledge.db`、`CODEKNOWLEDGE_DB_PATH` 上書き、更新はEXE上書き、移動時は `knowledge.db` + `-wal` + `-shm` も一緒に、OneDrive等の同期フォルダ禁止）
  4. MCPクライアント設定3種 — Cursor（`.cursor/mcp.json`, `mcpServers` キー）、GitHub Copilot in VS Code（`.vscode/mcp.json`, `servers` キー）、Claude Code（`claude mcp add --transport stdio --scope project code-knowledge -- <exe絶対パス>`）。`CodeKnowledge.Mcp.exe` の絶対パスを使う。形式は `spikes/phase0/README.md` を参照。
  5. Agent行動ルール — 要件11章の13項目のうち **Phase 1で有効なのは 1・2・8・9・13**。3〜7・10〜12 は Phase 2〜3 の Tool 依存なので「Phase 2以降で追加」と注記しつつ全文掲載。`CLAUDE.md` 等へコピーできるコードブロックとして記載。要件は `docs/code-knowledge-tool-requirements-v2.md` の11章。
  6. 検証記録 — Phase 0 README と同形式の表（自動テスト結果、発行成果物一覧、Claude Code実機検証、Cursor/Copilotは対象外の注記）。**この時点では実機検証行は「未実施」**。
  7. 要件からの変更点 — 設計書11章の Deviations を要約転記。**注意: 設計書の Deviations は現在4件ある**（当初計画の「3点」から Deviation #4 が追加済み。§6参照）。4件すべて載せるのが正確。
- コミット: `docs: add readme with client setup, db rules, and agent rules`

### Task 15: Phase 1完了ゲート（手動検証＋ユーザー承認）

- **Step 1**: `dotnet test ... --configuration Release` と発行を最終実行。`superpowers:verification-before-completion` の精神で、出力を確認してから「成功」と主張する（緑を憶測で書かない）。
- **Step 2**: Claude Code へ登録（`claude mcp add ... code-knowledge -- "<artifacts配下のexe絶対パス>"`）し、**ユーザーが**新セッションで `resolve_project → save_knowledge → search_knowledge → get_knowledge` の一連を実リポジトリで実行。DBが既定パス（EXE隣接）で動くことを確認。
- **Step 3**: README の検証記録へ実測値（テスト件数、`Get-ChildItem -File artifacts/mcp/win-x64` の成果物一覧、Claude Codeのバージョン・検証日・4 Toolの成否）を記入。`.mcp.json` に `code-knowledge` サーバー追加。コミット `docs: record phase 1 verification results`。
- **Step 4（ユーザー承認が必須・Codexは代行しない）**: ユーザーがPhase 1完了を承認したら、`docs/code-knowledge-tool-requirements-v2.md` の14章「Phase 1: 最小実用版」の各項目へ `[x]` と注記を付け、`docs: record phase 1 completion approval` でコミット。**承認が得られるまで Phase 2 へ進まない。**

> ⚠️ Step 2 の実機呼び出しと Step 4 の承認は**人間（ユーザー）の操作・判断**。Codex は承認を捏造せず、ここで停止してユーザーに実行・承認を依頼すること。

---

## 5. コミット時の運用ルール（このリポジトリの慣習）

- コミットメッセージ末尾に共著者トレーラを付ける（空行を1行挟む）。Codex に置き換えてよい:
  ```
  Co-Authored-By: <あなたの表示名> <noreply@…>
  ```
  （Claudeが付けていたのは `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`）
- 計画書 `docs/superpowers/plans/2026-07-11-code-knowledge-phase1.md` の各Stepの `- [ ]` は、タスク承認後に別コミット `docs: check off phase 1 plan task N` で `- [x]` へ更新している（Task 1〜13は更新済み）。同じ運用を続けてよい。
- **コミットするのはそのタスクが触るファイルだけ**。`git add -A` は使わない。

---

## 6. 計画コードからの確定した逸脱（**元に戻さないこと**）

Task 1〜13 の実装中、レビューで計画書の**逐語コード自体の欠陥**を多数発見し修正済み。計画書のコードブロックと実際のコミット済みコードが食い違う箇所があるが、**コミット済みが正**。計画書を読んで「計画と違う、直そう」としないこと。主なもの:

1. **テストcsproj**: `TestingPlatformDotnetTestSupport=true` が必要（無いと `dotnet test` が xunit.v3 テストを1件も発見せず0件で緑になる）。
2. **`Confidence` enum**: `JsonStringEnumConverter` + `JsonStringEnumMemberName` で "high"/"medium"/"low" の小文字文字列としてJSON化（既定の整数化は不可）。
3. **MigrationRunner**: バックアップは `File.Copy` ではなく **`VACUUM INTO`**（WAL内データ喪失を防ぐ）。v1スキーマに `idx_facts_version`/`idx_inferences_version`/`idx_relations_version` を追加済み。
4. **GitCommandRunner**: `RunBytes`（バイト忠実読取・BOM保持）を持ち、`ProcessStartInfo` に **`RedirectStandardInput = true`**（無いとstdioサーバーとして全ツールが30秒ハング）。remote解析はタブ分割 + ` (fetch)/(push)` 除去。
5. **ProjectIdResolver / RemoteUrlNormalizer**: config検証は専用述語 `IsNormalizedProjectId`（再正規化ではない）。ポート検出は scheme付きのみ、IPv6ブラケット対応、資格情報は最後の `@` まで除去。
6. **IProjectStore**: `FindByRepositoryRoot` ではなく **`FindStaleByRepositoryRoot(root, currentId) → IReadOnlyList<Project>`**。`project_id_changed` 警告は stale 行ごとに複数返る（要件5.8.2の無言孤立防止）。
7. **KeywordPreparation**: NFKC後にC0/DEL制御文字を除去（結合FTSクエリのクラッシュ防止）。
8. **ContentHasher**: 末尾改行のsplit空要素を行数から除外。
9. **SaveKnowledgeUseCase**: 実ファイル行数を超える StartLine を `invalid_arguments` で拒否（空文字列ハッシュの無言保存を防止）。明示的な `SqliteTransaction` 関連付け。
10. **SqliteKnowledgeStore**: `knowledge_fts` へ書き込む全テキストを NFKC 正規化。`SearchLike` は正規化済み fts 列を走査（全角/半角の表記揺れ吸収、要件8.4）。
11. **Program.cs**: 起動時に stderr へ notice、catch-all で `internal_error` + exit 1。
12. **エラー契約 = 文字列**: 設計書6章の構造化 `{code, message}` ではなく、`content[0].text` 内に `"{code}: {message}"` を**部分文字列**として含む（SDKが可変プレフィックスを付ける）。= 設計書 Deviations **#4**。

---

## 7. Phase 2 への申し送り（既知の制限・未解決事項）

Task 14 の本文には不要だが、Phase 2 で扱うべき事項。README や要件へ「既知の制限」として一言残すか、少なくとも記録として保持:

- **非UTF-8ファイルのハッシュ**: `ReadFileAtCommit` は非UTF-8を決定的な置換文字でデコードする。save/validate が同経路なら自己整合だが、Phase 2 の鮮度比較で偽陰性（別内容が同一ハッシュ）の残余リスク。
- **`database_busy` の挙動**: 公称 `busy_timeout=5000`（5秒）に対し、競合時は実測 **約35〜45秒**ブロックしてから `SQLITE_BUSY` が返る（マシン/負荷依存）。busy_timeout が効いているかの追調査は Phase 2。E2Eの当該テストは予算120秒で緑。
- **malformed な `save_knowledge`**: 必須フィールド欠落は MCP SDK のバインディング層で失敗し、`ToolGuard` を素通りして `"An error occurred invoking 'save_knowledge'."` のみになる（`{code}: {message}` にならない）。改善は Phase 2 候補。E2Eは現挙動をピン留め済み。
- **FTS候補200件上限**: FTS ヒットが200件超のとき、両ルート該当の項目が FTS top-200 から溢れると `matchedRoute` が "like" に降格し得る（Phase 1想定規模では許容、コード内コメント済み）。
- Phase 2 で追加予定の Tool/テーブルは設計書12章「対象外」を参照。

---

## 8. 参照ファイル

- 実装計画: `docs/superpowers/plans/2026-07-11-code-knowledge-phase1.md`（Task 14=L4072、Task 15=L4108）
- 設計書: `docs/superpowers/specs/2026-07-11-code-knowledge-phase1-design.md`（9章=ドキュメント、10章=完了ゲート、11章=Deviations 4件、12章=対象外）
- 要件定義: `docs/code-knowledge-tool-requirements-v2.md`（10.10=クライアント設定、11章=Agent行動ルール13項目、14章=Phase 1チェックリスト）
- Phase 0 README（README形式・検証記録の見本）: `spikes/phase0/README.md`

---

## 9. Task 1〜13 のコミット履歴（`e9e4b62..HEAD`、新しい順）

```
5828614 docs: check off phase 1 plan task 13
5fed099 fix: harden e2e test margins and pin no-persistence contract
2892ce5 test: add published exe end-to-end tests over mcp stdio
c478de9 docs: check off phase 1 plan tasks 11-12
fd33ae9 fix: redirect git stdin and harden mcp startup diagnostics
713d4e7 feat: add mcp stdio adapter exposing four knowledge tools
a8c1b91 test: pin version isolation and empty collection contracts for get detail
a01c0b1 feat: add get knowledge use case returning full detail
cc977ea docs: check off phase 1 plan tasks 7-10
0177176 fix: normalize search corpus with nfkc so width variants stay searchable
dc41520 feat: add hybrid fts and like search with merge ranking
e0d41e5 fix: reject evidence line ranges beyond actual file length
12a7001 feat: add save knowledge use case with transactional store and fts sync
56d418a feat: add file and symbol hash computation with whitespace normalization
9e48510 fix: strip control characters from search keywords
6d296c5 feat: add keyword normalization and query sanitization
d1b18f7 docs: check off phase 1 plan tasks 5-6
52464af fix: surface every stale project id sharing a repository root
ce470d9 feat: add resolve project use case with project store
f7d5d72 fix: strip credentials up to last at-sign before path
2a36a68 fix: validate configured project ids without renormalizing
52d4d46 fix: correct scp-style port misdetection and stabilize local project id
ba580e2 feat: add project id resolution with remote url normalization
4f998b8 docs: check off phase 1 plan tasks 1-4
f6db8b5 fix: preserve remote urls with spaces and byte-faithful file reads
732229e feat: add git cli adapter resolving repository context
d668ce5 fix: make pre-migration backup wal-safe and index version fk columns
7ae3397 feat: add sqlite migration runner with v1 schema
6c441f6 fix: serialize confidence as lowercase string in json contract
9b14a5f feat: add core domain model and error contract
83c7439 fix: enable dotnet test discovery for xunit.v3 (Microsoft.Testing.Platform)
e45b17a chore: scaffold phase 1 solution with core, infrastructure, mcp projects
```
