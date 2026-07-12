# Code Knowledge Phase 1 設計書

要件定義書: `docs/code-knowledge-tool-requirements-v2.md`（以下「要件」）
前提: Phase 0検証完了（`spikes/phase0/README.md`、2026-07-11ユーザー承認済み）

## 1. 目的

Phase 1では、要件14章「Phase 1: 最小実用版」を実装する。提供する機能は以下とする。

- Domain / Application / Infrastructure / MCPの責務分離（要件9.5）
- Gitリポジトリ解決とプロジェクトID生成（要件5章、9.6）
- SQLiteスキーマとマイグレーション基盤（要件12.2）
- FTS5（trigram）ハイブリッド検索（要件8章）
- MCP Tool: `resolve_project`、`search_knowledge`、`get_knowledge`、`save_knowledge`
- 最新確定バージョンのみの管理
- Agent向け利用ルールと3クライアント向けMCP設定手順のドキュメント

Phase 2以降の機能（鮮度検証、差分比較、保守Tool）は実装しない。ただしPhase 2が必要とする`file_hash`・`symbol_hash`は、Phase 1の保存時から計算・記録する（保存時点のハッシュがなければPhase 2の鮮度検証が機能しないため）。

Phase 0のソースコードは昇格させず、本設計に従って`src/`配下へ新規実装する。

## 2. 採用方式

### 2.1 プロジェクト構成（3プロジェクト構成）

```text
ck/
├─ CodeKnowledge.slnx            # Phase 1本番ソリューション（新設）
├─ CodeKnowledge.Phase0.slnx     # spike回帰検証用として残す
├─ src/
│  ├─ CodeKnowledge.Core/            # Domain + Application（フォルダで区分）
│  ├─ CodeKnowledge.Infrastructure/  # SQLite, FTS5, Git CLI, ハッシュ計算
│  └─ CodeKnowledge.Mcp/             # MCP Toolアダプター（EXE）
├─ tests/
│  ├─ CodeKnowledge.Core.Tests/
│  ├─ CodeKnowledge.Infrastructure.Tests/
│  └─ CodeKnowledge.Mcp.Tests/       # 発行EXEを起動するE2E含む
└─ spikes/phase0/                    # 変更しない
```

依存方向は`Mcp → Core ← Infrastructure`とする。InfrastructureはCoreが定義するインターフェースを実装し、McpがDIで束ねる。CoreはMCP SDK・SQLite・Gitのいずれにも依存しない（要件9.5.2、AC-11、AC-12）。

不採用とした方式:

- Domainを独立アセンブリにする4プロジェクト構成は、この規模（Tool 4つ、エンティティ7種）ではプロジェクト間調整のコストが利得を上回る。参照方向の強制はCoreがInfrastructure参照を持たないことで担保できる。
- CoreとInfrastructureを同居させる2プロジェクト構成は、「ApplicationからSQL・Gitを直接触らない」境界がフォルダ規約だけになるため不採用。

### 2.2 実行ファイルと発行

- EXE名は`CodeKnowledge.Mcp.exe`とする（要件2.2）。
- framework-dependent・単一ファイル・`win-x64`発行とする（Phase 0で実証済み）。
- 引数なしで起動するとMCP stdioサーバーとして動作する。stdoutはMCP通信専用とし、ログはstderrへ出力する。

## 3. 起動フローと構成解決

起動シーケンスは以下とする。

1. **DBパス解決**: 環境変数`CODEKNOWLEDGE_DB_PATH`が設定されていればその値、なければ`AppContext.BaseDirectory`直下の`knowledge.db`。プロセスの作業ディレクトリ（CWD）は使用しない（MCPクライアントごとにCWDが不定のため）。
2. **接続初期化**: SQLite接続ごとに`PRAGMA journal_mode = WAL`、`busy_timeout = 5000`、`foreign_keys = ON`を明示設定する。
3. **マイグレーション**: `PRAGMA user_version`を確認し、要求バージョンより古い場合は前方向マイグレーションを排他トランザクションで順次適用する。適用前に`knowledge.db.bak-<version>`を同フォルダへコピーする。`user_version`が対応範囲を超える場合は`schema_version_unsupported`をstderrへ出力し、DBを変更せず非0終了する。複数プロセス同時起動時は排他トランザクションで直列化し、後着は適用済みを検知してスキップする。
4. **MCPサーバー開始**: `Microsoft.Extensions.Logging`のコンソールロガーをstderrへ明示構成する（要件13.5）。

### 3.1 DB既定パスに関する要件からの変更

要件10.10は既定DBパスを`%LOCALAPPDATA%\CodeKnowledge\knowledge.db`と定めるが、本設計では**EXEと同じフォルダの`knowledge.db`**へ変更する（2026-07-11ユーザー決定）。

理由: DBの場所が自明になり、フォルダごと渡すポータブル運用ができる。

この方式の制約として、以下をREADMEへ明記する。

- EXEを別フォルダへコピーするとDBも分裂する。バージョン更新はEXEの上書き置換とし、フォルダ移動時はDBファイル（WAL・SHMファイル含む）も一緒に移動する。
- OneDrive等の同期フォルダへ配置しない（SQLite WALと同期ソフトの競合による破損リスク）。

環境変数`CODEKNOWLEDGE_DB_PATH`による上書きは、テストでの一時DB分離と特殊構成のために提供する。

## 4. DBスキーマ（マイグレーションv1）

マイグレーションv1ではPhase 1で使用する10テーブルのみを作成する。`knowledge_diffs`、`knowledge_diff_changes`、`temporary_comparisons`はPhase 3のマイグレーションで追加する（2026-07-11ユーザー決定）。

共通規約:

- PKはすべてTEXTとし、`Guid.CreateVersion7()`で生成する（時系列ソート可能）。
- 日時はISO 8601 UTCのTEXTで保存する。
- FKには`ON DELETE`動作を明示し、`foreign_keys = ON`前提で設計する。

| テーブル | 主な列 | 制約 |
|---|---|---|
| `projects` | `project_id`(PK), `display_name`, `repository_root`, `remote_url`, `created_at`, `updated_at` | `remote_url`は正規化済み・認証情報なし |
| `knowledge` | `id`(PK), `project_id`(FK), `canonical_key`, `title`, `current_version_id`(FK, null許容), `created_at`, `updated_at` | `UNIQUE(project_id, canonical_key)` |
| `knowledge_versions` | `id`(PK), `knowledge_id`(FK), `commit_hash`, `branch_name`(null許容), `original_question`, `summary`, `confidence`, `tags`, `created_at`, `created_by`, `retain`, `retain_reason` | `confidence`はTEXT + CHECK（high/medium/lowのみ） |
| `facts` | `id`(PK), `knowledge_version_id`(FK), `text`, `sort_order` | |
| `fact_evidence` | `fact_id`(FK), `evidence_id`(FK) | 複合PK。「事実には根拠必須」はApplication層バリデーションで強制 |
| `inferences` | `id`(PK), `knowledge_version_id`(FK), `text`, `confidence`, `reason`, `sort_order` | `confidence`はCHECK（high/medium/low） |
| `inference_evidence` | `inference_id`(FK), `evidence_id`(FK) | 複合PK |
| `evidence` | `id`(PK), `knowledge_version_id`(FK), `file_path`, `symbol_id`, `symbol_name`, `symbol_kind`, `signature`, `start_line`, `end_line`, `commit_hash`, `file_hash`, `symbol_hash`, `reason` | `file_path`はリポジトリ相対・`/`区切り |
| `relations` | `id`(PK), `knowledge_version_id`(FK), `from_symbol`, `to_symbol`, `kind` | `kind`はCHECK（要件6.4の9種別のみ） |
| `knowledge_fts` | FTS5仮想テーブル（trigram）。列: `title`, `original_question`, `summary`, `facts`, `inferences`, `tags`, `symbol_names`, `symbol_ids`, `file_paths`, `canonical_key`, `knowledge_id`(UNINDEXED), `project_id`(UNINDEXED) | 最新確定バージョンのみ。昇格トランザクション内の明示delete + insertで同期（要件12.1） |

### 4.1 tagsの追加（要件の隙間の解消）

要件8.2は「タグ」を検索対象に挙げるが、要件6.2のKnowledgeVersionモデルにtags項目がない。本設計では`knowledge_versions.tags`（スペース区切りTEXT、`save_knowledge`の任意入力）として追加する。要件定義書へのフィードバック対象とする。

### 4.2 ドメインモデル

CoreにProject / Knowledge / KnowledgeVersion / Fact / Inference / Evidence / RelationのC# recordを定義する。テーブルとほぼ1対1とし、`confidence`はC#では列挙型（`Confidence.High/Medium/Low`）で表現する。

## 5. ユースケース処理フロー

CoreのApplication部に4つのユースケースサービスを置く。MCP Toolハンドラーは入出力変換のみを行い、SQL・Gitコマンドを直接実行しない（要件9.5.2）。

### 5.1 ResolveProject（全Toolの冒頭で共通実行）

1. `git rev-parse --show-toplevel`でリポジトリルートを解決する。失敗時は`git_repository_required`、`git`不在時は`git_not_found`で処理を中断する。
2. `project_id`を以下の優先順位で決定する: (1) `git config codeknowledge.projectId`（要件5.3.2の形式検証を行い、不正なら`invalid_arguments`） (2) remote URLの正規化（要件5.3.2の8ルール。remote選択はorigin > upstream > 辞書順） (3) `local:<正規化パスのSHA-256先頭16文字>`。由来を`projectIdSource`（config / remote / local）として返す。
3. `display_name`は`git config codeknowledge.projectName` > remoteリポジトリ名 > Gitルートディレクトリ名の順で解決する。
4. `projects`テーブルへupsertする。同一`repository_root`で異なる`project_id`の既存行があれば`project_id_changed`警告（旧ID・対象ナレッジ件数付き）を出力へ含める。同一`project_id`で`repository_root`が異なる場合は現在値へ更新する（要件5.8.3）。
5. HEADコミットとブランチ名を取得する。detached HEADではブランチ名をnullとする。コミット取得失敗時は保存系処理を中断する（AC-14）。

解決結果は`repository_root`と`git config`の変更検出を条件にプロセス内キャッシュしてよいが、コミットハッシュは都度取得する（要件5.9）。

### 5.2 SearchKnowledge（ハイブリッド検索、要件8章）

1. キーワードをNFKC正規化し、空になったものを破棄する。全キーワードが無効なら`invalid_arguments`とする。
2. Unicodeコードポイント数で振り分ける。3以上はFTSルート: `"`を`""`へ置換し全体をダブルクォートで囲み、` OR `で結合して単一MATCHパラメータとし、`bm25()`昇順で順位付けする。1〜2はLIKEルート: `\` `%` `_`をエスケープし`LIKE @pattern ESCAPE '\'`で最新確定バージョンの実テーブルを走査する。LIKEルートの対象はタイトル、元質問、概要、事実、推論、タグ、シンボル名、canonical keyとし、ファイルパスは対象外とする（要件8.3）。
3. マージ順位: 両ルートヒット（bm25昇順）→ FTSのみ（bm25昇順）→ LIKEのみ（ヒットキーワード数降順 → 更新日時降順）。各件に`matchedRoute`と`matchedKeywords`を含める。
4. FTSルートでキーワード単位の構文エラーが発生した場合、当該キーワードのみ除外して継続する。
5. すべてパラメータ化SQLとし、`WHERE project_id = @currentProjectId`を必ず適用する。`limit`既定10・上限50。0件は空配列を返す（エラーにしない）。

### 5.3 SaveKnowledge

1. 入力バリデーション: `confidence`が列挙値以外なら`invalid_arguments`。根拠を1件も参照しないfactが含まれる場合は`fact_requires_evidence`として保存全体を拒否する。
2. `evidence.file_path`をリポジトリ相対・`/`区切りへ正規化する。
3. 保存コミット時点の`file_hash`（ファイル全体）と`symbol_hash`（`start_line`〜`end_line`のテキスト範囲。行末空白除去・改行コード統一・連続空白縮約の正規化後にハッシュ計算。要件9.4段階2）を計算する。ハッシュ計算はAgentに行わせない（要件10.6）。
4. 単一トランザクションで保存する: `canonical_key`が完全一致する既存Knowledgeがあれば新バージョン追加、なければ新規Knowledge作成 → `current_version_id`更新 → `knowledge_fts`の旧行delete + 新行insert。
5. コミット後、類似`canonical_key`または類似タイトルを検索し、`similarKnowledge`警告（候補一覧）を出力へ含める。類似判定はMVPでは単純な部分一致（正規化済みキー・タイトルの相互包含）とする。

### 5.4 GetKnowledge

`knowledgeId`（+任意の`versionId`）を受け取り、facts / inferences / evidence / relations / confidence / tagsを構造化して返す。`versionId`省略時は最新確定バージョンを返す。存在しない場合は`knowledge_not_found`とする。

## 6. MCP Tool契約

Tool名・入出力は要件10.1〜10.3、10.6に従う。全Toolが`workingDirectory`を受け取り、冒頭でResolveProjectを実行する。Agentが渡す`projectId`類は信用しない（要件16章）。

エラーは全Tool共通で以下の形式とする。

```json
{ "code": "git_repository_required", "message": "..." }
```

Phase 1のエラーコード: `git_repository_required` / `git_not_found` / `invalid_arguments` / `fact_requires_evidence` / `knowledge_not_found` / `schema_version_unsupported` / `database_busy`（busy timeout超過） / `internal_error`。

## 7. Infrastructure実装方針

### 7.1 Git（`IGitRepository`）

- `git` CLIへのシェルアウトで実装する（要件9.6）。LibGit2Sharp等は使用しない。
- 引数はプロセス引数配列で渡し、シェル経由にしない。タイムアウトは既定30秒。
- 標準出力・標準エラーはUTF-8として扱い、`core.quotepath`によるパスエンコーディングを考慮する。

### 7.2 SQLite

- `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3`（Phase 0で実証済みのバージョン系列）。
- ORMは使用せず、パラメータ化した素のSQLをリポジトリクラスに集約する。
- busy timeout超過（`SqliteException` SQLITE_BUSY）は`database_busy`エラーへ変換する。

### 7.3 ログ

構造化ログ（Tool名、project_id、knowledge_id、検索件数、保存結果、エラー種別）をstderrへ出力する。秘密情報・コード本文はログに出力しない。

## 8. テスト戦略

TDD（Red-Green-Refactor）で実装する。

1. **Core.Tests（単体）**: ユースケースをInfrastructureインターフェースのフェイク実装で直接テストする（AC-11）。remote URL正規化、localフォールバック、キーワードサニタイズ、振り分け、マージ順位、fact根拠必須、confidence検証、canonical_key一致時のバージョン追加などの要件ロジックをここで網羅する。
2. **Infrastructure.Tests（統合）**: 一時DBで実SQLiteを使用し、FTS5 trigram検索（日本語含む）、マイグレーション（v0→v1、バックアップ作成、`schema_version_unsupported`）、トランザクション整合性を検証する。一時Gitリポジトリを実際に作成してGit CLIアダプター（worktree解決、config読み取り、detached HEAD、remoteなし）を検証する。
3. **Mcp.Tests（E2E）**: 発行済みEXEをプロトコルテストクライアントから起動し、4 Toolの呼び出し、`CODEKNOWLEDGE_DB_PATH`による一時DB分離、stdoutへのログ混入なし、Gitリポジトリ外での`git_repository_required`を検証する。

テストはPhase 0と同じ隔離規約に従う: 一時ディレクトリ・一時DBのみ使用し、本物のDBに触れない。テスト終了時にWAL・SHMファイルごとクリーンアップする。

受け入れ条件のマッピング: Phase 1該当分（AC-01〜03、09〜13、15〜17、19〜23、27、28）を対応するテストケースへ明示的にマッピングし、実装計画に含める。

## 9. ドキュメント成果物

リポジトリルートの`README.md`（新規作成）に以下を記載する。

1. 3クライアント（Cursor / GitHub Copilot in VS Code / Claude Code）のMCP設定手順（要件10.10のパスを実際の配置に合わせて記載）
2. DB配置ルール: EXE隣接が既定、`CODEKNOWLEDGE_DB_PATH`で上書き、更新はEXE上書き置換、同期フォルダ禁止
3. Agent行動ルールファイルのテンプレート（要件11章の13項目）
4. 検証記録（Phase 0 READMEと同じ形式）

## 10. Phase完了ゲート

Phase 1は以下をすべて満たした場合にのみ完了とする。

1. すべての自動テストが成功する。
2. 発行済み`CodeKnowledge.Mcp.exe`のE2Eテストが成功する。
3. Claude Codeから実機で4 Toolを呼び出し、「保存 → 検索 → 取得」の一連が実際のリポジトリで動作する。
4. READMEに9章の成果物が記載されている。
5. 要件定義書との乖離（DB既定パス変更、tags追加）がDeviationsとして記録されている。
6. ユーザーがPhase 1完了を承認する。

CursorとGitHub Copilot in VS Codeの実機検証は、Phase 0と同様にユーザー判断で対象外とする（2026-07-11決定）。設定手順のドキュメント整備のみ必須とする。

## 11. 要件定義書からの変更点（Deviations）

| # | 変更 | 理由 | 決定 |
|---|---|---|---|
| 1 | DB既定パスを`%LOCALAPPDATA%\CodeKnowledge\knowledge.db`からEXE隣接の`knowledge.db`へ変更。環境変数`CODEKNOWLEDGE_DB_PATH`で上書き可能 | DB位置の自明性とポータブル運用 | 2026-07-11ユーザー承認 |
| 2 | `knowledge_versions.tags`を追加（要件6.2のモデルにない検索対象「タグ」の実現手段） | 要件8.2と6.2の不整合の解消 | 本設計で確定、要件へフィードバック |
| 3 | 実機検証はClaude Codeのみ（要件2.1は3クライアント） | Cursor / Copilotの実施環境がない | 2026-07-11ユーザー承認（Phase 0と同判断） |
| 4 | ツールのエラー契約は6章の構造化JSONオブジェクト`{code, message}`ではなく、`McpException`経由で`content[0].text`内の文字列`"{code}: {message}"`として表面化する（SDKが`An error occurred invoking 'x': ...`のような可変プレフィックスを付ける）。クライアントは`"<code>: "`を部分文字列として照合すること | ModelContextProtocol SDKのエラー表面仕様 | 2026-07-12レビュー（コーディネーター承認） |

## 12. 対象外

Phase 1では以下を実装しない。

- `validate_knowledge`、`compare_knowledge`、`save_comparison`、`delete_orphan_versions`、`migrate_project`（Phase 2〜3）
- `knowledge_diffs`、`knowledge_diff_changes`、`temporary_comparisons`テーブル（Phase 3のマイグレーションで追加）
- 鮮度判定ロジック（ハッシュの記録のみ行い、比較はPhase 2）
- CLI（`CodeKnowledge.Cli`）
- サーバー側同義語展開、Embedding、Roslynハッシュ（Phase 4）
- 旧バージョンの削除処理（`retain`列は保存のみ）
