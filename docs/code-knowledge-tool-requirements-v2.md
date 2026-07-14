# Code Knowledge Tool 要件定義書 v2.0

---

## 改訂サマリー（v1 → v2）

blindspot pass（技術調査・観点レビュー）の結果を反映した改訂版。主な変更点は以下の通り。

| # | 変更内容 | 該当箇所 |
|---|---|---|
| 1 | 検索要件を全面改訂。trigramトークナイザ採用、文字数によるハイブリッド検索、クエリサニタイズ、Agent側キーワード展開要件を確定 | 8章 |
| 2 | プロジェクトID解決を詳細化。remote URL正規化ルール、remote複数時の優先順位、明示上書き設定、project_id変遷時の孤立対策を新設 | 5章 |
| 3 | Git操作は`git` CLIへのシェルアウトで実装することを確定（worktree対応・`git config`読み取り要件のため） | 9.6 |
| 4 | スキーママイグレーション要件を新設（`PRAGMA user_version`による版管理） | 12.2 |
| 5 | 「事実には根拠必須」の強制方法を具体化（`fact_evidence`中間テーブル＋Application層バリデーション） | 6.2, 12章 |
| 6 | 一時比較結果のライフサイクル（プロセス跨ぎ・TTL・クリーンアップ）を定義 | 7.3 |
| 7 | ダーティなワーキングツリーの扱いを鮮度検証に追加（`isWorkingTreeDirty`） | 9.2, 10.4 |
| 8 | `is_current`カラムを廃止し`current_version_id`に一本化（二重管理の排除） | 6.2 |
| 9 | detached HEAD時の`branch_name`をnull許容として明記 | 6.2 |
| 10 | Evidenceのファイルパス正規化ルール（リポジトリ相対・`/`区切り）を明記 | 6.3 |
| 11 | canonical_keyの重複乱立防止要件（保存前の類似キー検索）を追加 | 6.1, 10.6 |
| 12 | `migrate_project` Toolを新設 | 10.9 |
| 13 | 配布形態（framework-dependent単一ファイル発行、.NET 10ランタイム導入済み前提）を明記 | 2.2 |
| 14 | 実装前のspike検証タスクをPhase 0として新設 | 14章 |
| 15 | FTS同期はトリガーではなく昇格トランザクション内の明示更新とする | 12.1 |
| 16 | 受け入れ条件・禁止事項を上記に合わせて追加 | 15, 16章 |
| 17 | UC-06（新規調査結果の保存）を新設。「保存提案・同意確認」はAgent行動ルール、「保存」はMCPの責務として分担を明記 | 4章, 11章 |
| 18 | 確信度（confidence）を3段階列挙値（high/medium/low）として定義。判定基準・適用範囲・数値不採用の理由を明記 | 6.2.1 |
| 19 | 未決事項をすべて決定し解消（LIKE対象カラム、シンボルハッシュ開始段階、保守ToolのMCP公開、配布形態）。18章を決定記録として保持 | 18章ほか |

技術調査で確認済みの前提: MCP C# SDK（`ModelContextProtocol`パッケージ）はv1.0が2026年2月に安定版リリース済みであり、.NET 10 + stdio構成は公式サポートの本流である。SQLite FTS5のtrigramトークナイザは3.34.0以降の内蔵機能であり、外部拡張DLLなしで利用できる（バンドル版SQLiteでの動作確認はPhase 0で実施）。

---

## 1. 目的

本システムは、AI Agentが既存ソフトウェアのコード調査結果をプロジェクト単位で保存・検索・再利用し、同一箇所を毎回ゼロから調査するコストを削減するためのMCPサーバーである。

主な目的は以下の3点とする。

1. 一度調査したコード仕様・処理フロー・設計上の事実を保存し、次回以降の回答に再利用する。
2. ユーザーから既存機能について質問された際、保存済みナレッジを検索し、AI Agentがわかりやすく説明できるようにする。
3. 前回調査時点と任意のGitコミットまたは現在のHEADとの差分を調査し、変更内容を説明したうえで、必要に応じて最新ナレッジと差分情報を保存する。

本システムは、単なるMarkdownメモ管理ではなく、Gitコミットおよび根拠コードに基づいて鮮度を検証できる「コード知識キャッシュ」として実装する。

### 1.1 MVPにおける提供形態

MVPではAI Agentから利用するためのMCPインターフェースを提供する。

ただし、将来的にCLIから同じ機能を利用できるよう、検索・保存・検証・比較・Git操作・SQLiteアクセスなどの本体ロジックをMCPハンドラーへ直接実装してはならない。

MCPはApplication層を呼び出す薄いアダプターとして実装する。

想定構成:

```text
CodeKnowledge.Core / Application
    ├─ プロジェクト解決
    ├─ ナレッジ検索
    ├─ ナレッジ保存
    ├─ 鮮度検証
    └─ 差分比較

CodeKnowledge.Infrastructure
    ├─ SQLite
    ├─ Git（CLIアダプター）
    └─ FTS5

CodeKnowledge.Mcp
    └─ MVPで提供するAgent向けアダプター

CodeKnowledge.Cli
    └─ 将来追加する人間・スクリプト向けアダプター
```

MVPでは`CodeKnowledge.Cli`の実装は必須としない。

---

## 2. 想定利用環境

- OS: Windows 11
- Runtime: .NET 10
- 実装言語: C#
- データベース: SQLite
- 全文検索: SQLite FTS5（trigramトークナイザ）
- MCPクライアント: CodexなどのAI Agent
- MCP実装: 公式C# SDK（`ModelContextProtocol`パッケージ、v1.x安定版）
- Gitリポジトリ配下で利用する
- 対象ディレクトリから親方向へ探索してGitリポジトリルートを解決できること
- `.git`ディレクトリまたはGit worktreeの管理情報を解決できない場合、本ツールは利用不可とする
- 社用PCで利用するため、常駐DBサーバー、Docker、外部検索エンジン、管理者権限を必要としない構成とする
- NuGetパッケージの利用は許容する
- 初期実装では外部SQLite拡張DLLおよびベクトルDBを使用しない
- `git`コマンドがPATH上で利用可能であること（9.6参照）

### 2.1 対応AI Agent

初期対応対象は以下とする。

- Cursor
- GitHub Copilot
- Claude Code

本システムは標準MCPサーバーとして実装し、Agent固有APIへの依存を避ける。

ただし、MCPサーバー本体を共通化しても、各Agentで以下は個別に設定する必要がある。

- MCPサーバーの登録方法および設定ファイル
- ローカルプロセスの起動コマンド
- 環境変数
- Tool実行の許可設定
- AgentへMCP Toolの利用を促すルールファイル

初期実装では、3 Agentすべてから利用しやすいローカルMCP transport（stdio）を採用する。特定クライアント専用の拡張機能には依存しない。

Tool名、入力JSON Schema、出力形式、エラーコードは全Agentで共通とする。

Agentごとのモデル性能やTool選択方針により、自動的にToolを呼び出す確実性は異なる。そのため、各Agentのルール設定で以下を要求する。

1. 既存仕様の調査前に`search_knowledge`を呼び出す。
2. ナレッジ利用前に必要に応じて`validate_knowledge`を呼び出す。
3. 差分調査では`compare_knowledge`を使用する。
4. 永続化はユーザーの明示指示、またはAgentの保存提案へのユーザー同意があった場合に限り`save_knowledge`または`save_comparison`を使用する（UC-06参照）。

### 2.2 配布形態

- 対象マシンには.NET 10ランタイムが導入済みであることを前提とする（Visual Studio導入済みの開発者マシンを想定）。
- framework-dependent・単一ファイル発行（`PublishSingleFile`）を基本とする。
- 配布物は`CodeKnowledge.Mcp.exe`単体（および必要な場合の同梱ネイティブライブラリ）とし、インストーラーを必要としない。
- 管理者権限なしで任意のユーザーディレクトリへ配置して動作すること。
- ランタイム未導入マシンへの配布はMVPのスコープ外とする。将来必要になった場合はself-contained発行へ切り替えるが、これはビルド構成の変更のみで対応でき、コード変更を伴わないこと（本項は要件であり、未決事項ではない）。
- 前提とする.NET 10ランタイムは対象マシンに導入済みである（開発者マシンで.NET 10による開発を行っているため確認済み）。

---

## 3. スコープ

### 3.1 対象

- 既存ソフトウェアのコード調査結果
- 業務機能の仕様
- 処理フロー
- 呼び出し関係
- 設定値やDI登録
- 関連するテスト
- Gitコミット間の意味的な変更内容
- 調査結果の根拠となるファイルおよびシンボル

#### 3.1.1 必須前提条件

本ツールはGitリポジトリ専用とする。

以下のいずれかに該当する場合、対象プロジェクトとして扱わない。

- カレントディレクトリおよび親ディレクトリにGitリポジトリが存在しない
- `git rev-parse --show-toplevel`が失敗する
- 現在のコミットハッシュを取得できない
- 対象がGit管理外の単なるフォルダである

`.git`ディレクトリが物理的に存在する通常リポジトリだけでなく、Git worktreeやsubmoduleなど、Gitコマンドで正しくリポジトリとして解決できる構成は許可する。

Gitリポジトリとして解決できない場合、検索、保存、検証、比較の各処理は実行せず、明確なエラーを返す。

### 3.2 対象外

初期バージョンでは以下を対象外とする。

- リポジトリ全体の完全なコードインデックス作成
- 全コミットのナレッジ履歴保存
- 全シンボルの静的解析グラフ構築
- ベクトルDBの導入
- Qdrant、Elasticsearch、Milvusなどの外部検索サービス
- Dockerまたは常駐サービスの必須化
- ソースコード自体の全文保存
- 自動的な仕様書生成の完全保証
- MVPでのCLI実装
- Git履歴の代替
- 形態素解析トークナイザ（MeCab、Sudachi等）の導入
- サーバー側の同義語辞書・クエリ自動展開

Gitがコード履歴を保持するため、本システムは調査結果、意味的な差分、根拠情報のみを保持する。

---

## 4. 主要ユースケース

### UC-01: 過去の調査結果を再利用する

ユーザー例:

> 注文完了メールの処理ってどうなってたっけ？

処理:

1. Agentは現在の作業ディレクトリから対象プロジェクトを解決する。
2. 対象プロジェクト内のナレッジのみを検索する。
3. 関連ナレッジが存在する場合、ナレッジと根拠情報を取得する。
4. 必要に応じて鮮度を検証する。
5. Agentは取得したナレッジを元にユーザー向けの説明を生成する。

期待結果:

- 別プロジェクトのナレッジが混入しない。
- 保存済み情報が利用可能な場合、コード全体の再調査を避けられる。
- 回答には、必要に応じて根拠ファイルやシンボルを含められる。

### UC-02: 保存済みナレッジを元に仕様を説明する

ユーザー例:

> このメール機能の仕様がよくわからない。前に調べた内容があれば、それを元に説明して。

処理:

1. Agentは自然言語の質問から検索語を生成する（8.5のキーワード展開要件に従う）。
2. MCPの検索Toolを呼び出す。
3. MCPはハイブリッド検索（8.3）により、タイトル、元質問、概要、事実、タグ、シンボル名、ファイルパスを検索する。
4. Agentは取得結果を再構成し、ユーザーの理解度や目的に合う形で説明する。

MCPは最終的な説明文を生成する必要はない。MCPの責務は、関連ナレッジ、根拠、鮮度、関連シンボルを構造化データとして返すこととする。

### UC-03: 前回調査時点と現在の差分を調査する

ユーザー例:

> 前に調査してもらったメール仕様、今も同じ？

処理:

1. 対象ナレッジの最新確定バージョンを取得する。
2. 保存済みの調査コミットを比較元とする。
3. 現在のHEADを比較先とする。
4. 根拠ファイル、根拠シンボル、関連設定、関連DI登録などの変更を確認する。
5. 変更なし、部分変更、全面的な陳腐化を判定する。
6. Agentは変更箇所のみ追加調査する。
7. 変更内容と現在の仕様を説明する。

### UC-04: 前回調査時点と指定コミットの差分を調査する

ユーザー例:

> 前回調査したバージョンとコミット `def456` でどこが変わった？

処理:

1. 比較元は対象ナレッジの最新確定バージョンのコミットとする。
2. 比較先はユーザー指定コミットとする。
3. Git上で比較可能か検証する。
4. 根拠コードおよび関連箇所の差分を取得する。
5. Agentが差分を意味的に分析する。
6. MCPは一時的な比較結果を返す。
7. ユーザーが保存を指示した場合のみ、比較結果と新しいナレッジバージョンを保存する。

### UC-05: 差分調査結果を保存する

ユーザー例:

> なるほど。そのバージョンの差分も保存しといて。

処理:

1. 直前に生成した一時比較結果を特定する（7.3のライフサイクル定義に従う）。
2. `KnowledgeDiff`として保存する。
3. 比較先コミット時点のナレッジを`KnowledgeVersion`として保存する。
4. 必要に応じて新しいバージョンを最新確定バージョンに昇格する。
5. 比較元バージョンは、保存済み差分から参照される限り保持する。

### UC-06: 新規調査結果をナレッジとして保存する

ユーザー例（明示指示）:

> 今回調査した結果をナレッジに貯めておいて。

またはAgentからの提案（保存提案）:

> （Agentが新規調査を完了した後）今回の調査結果をナレッジとして保存しますか？次回から再調査なしで回答できます。

処理:

1. Agentは調査結果を事実（facts）と推論（inferences）に分離し、事実には根拠（evidence）を紐付けて構造化する。
2. `search_knowledge`で既存の類似ナレッジを確認する。
3. 同一テーマの既存Knowledgeがある場合は新バージョン追加、なければ`canonical_key`を新規採番して`save_knowledge`を呼び出す。
4. MCPは`projectId`、ハッシュ類、作成日時などの機械的情報を補完して保存する。
5. `similarKnowledge`警告が返された場合、Agentはユーザーへ重複の可能性を通知する。

期待結果:

- ユーザーの明示指示、またはAgentの保存提案へのユーザー同意があった場合のみ保存される。
- Agentがユーザーの意思確認なしに自動保存しない。

責務分担（本ユースケースの実現方式）:

- **「保存を提案する・ユーザーに聞く」はAgentの責務**であり、Agent行動ルール（11章）で規定する。MCPサーバーはリクエスト/レスポンス型のToolであり、サーバー側から自発的にユーザーへ問いかけることはできない。
- **「保存する」はMCPの責務**であり、`save_knowledge`（10.6）で実現する。
- MCP仕様のelicitation（Tool実行中のユーザー入力要求）は、対応AI Agent間でサポート状況が均一でないため、MVPでは使用しない。保存意思の確認はAgentの会話フローで行う。

---

## 5. プロジェクト識別要件

### 5.1 目的

別プロジェクトのナレッジが検索結果に混入することを防ぐ。

すべての検索、取得、検証、保存処理は、原則として現在のプロジェクトをスコープとして実行する。

### 5.2 プロジェクト表示名の解決順序

`display_name`は以下の優先順位で解決する。

1. `git config --get codeknowledge.projectName`
2. `remote.origin.url`から抽出したリポジトリ名
3. Gitルートディレクトリ名

`codeknowledge.projectName`は本システム独自の任意設定とする。

例:

```bash
git config codeknowledge.projectName "Order API"
```

### 5.3 プロジェクトIDの解決

#### 5.3.1 基本形式

remoteが存在する場合:

```text
<normalized-host>/<normalized-path>
```

例:

```text
github.com/company/order-api
git.example.local/team/order-system
git.example.local:8443/team/order-system
```

remoteが存在しない場合:

```text
local:<repository-root-hash>
```

#### 5.3.2 remote URLの正規化ルール

同一リポジトリがclone方法（SSH/HTTPS）やURL表記の揺れによって別プロジェクトとして分裂することを防ぐため、`project_id`の生成時にはremote URLへ以下の正規化を順に適用する。

1. **形式の統一**: scp形式（`git@host:path`）はURL形式（`host/path`）へ変換する。
2. **スキームの除去**: `https://` `http://` `ssh://` `git://` を除去する。
3. **認証情報の除去**: `user@` および `user:password@` を除去する。パスワードやトークンが含まれる場合でも、`project_id`および保存される`remote_url`に認証情報を含めてはならない。
4. **ポート番号の扱い**: ポート番号が明示されている場合は保持する（`host:8443/...`）。標準ポートの省略表記と明示表記の同一視は行わない。
5. **`.git`サフィックスの除去**: パス末尾の`.git`を除去する。
6. **末尾スラッシュの除去**: パス末尾の`/`を除去する。
7. **小文字化**: ホスト名およびパス全体を小文字化する。
8. **パス区切りの統一**: `\`は`/`へ統一する。

正規化の例:

```text
git@github.com:Company/Order-API.git
https://github.com/company/order-api
https://user:token@github.com/Company/order-api.git
ssh://git@github.com/company/order-api

→ すべて github.com/company/order-api
```

備考: パスの小文字化は、ホスティングサービスによっては理論上異なるリポジトリを同一視する可能性があるが、表記揺れによるナレッジ分裂の実害の方が大きいと判断し、小文字化を採用する。

#### 5.3.3 remote選択の優先順位

複数のremoteが存在する場合、`project_id`の生成に使用するremoteは以下の優先順位で決定する。

1. `origin`
2. `upstream`
3. 上記が存在しない場合、remote名の辞書順で最初のもの

選択結果は決定的でなければならない。同一リポジトリ・同一remote構成に対して、実行タイミングやOSによって異なる`project_id`が生成されてはならない。

#### 5.3.4 localフォールバックの仕様

remoteが1つも存在しない場合、以下の手順で`project_id`を生成する。

1. Gitリポジトリルートの絶対パスを取得する。
2. パス区切りを`/`へ統一し、全体を小文字化する（ドライブレター含む。例: `c:/work/my-tool`）。
3. 正規化後のパスをUTF-8でエンコードし、SHA-256ハッシュを計算する。
4. ハッシュの16進表現の先頭16文字を使用する。

```text
local:3fa2b8c1d4e5f607
```

備考: localプロジェクトはマシン・パス依存であり、別マシンや移動後のパスでは別プロジェクトになる。これはMVPの制約として許容する（5.8参照）。

### 5.4 保存項目

プロジェクト情報として最低限以下を保存する。

- `project_id`
- `display_name`
- `repository_root`
- `remote_url`（正規化済み・認証情報を含まない）
- `created_at`
- `updated_at`

### 5.5 検索スコープ

初期バージョンでは、すべてのナレッジをプロジェクト単位で完全分離する。

検索時は必ず以下に相当する条件を適用する。

```sql
WHERE project_id = @currentProjectId
```

初期バージョンでは`global`スコープおよび全プロジェクト横断検索を実装しない。

### 5.6 Project IDとRepository Rootの役割

- `repository_root`: 現在の作業場所を特定するために使用する
- `project_id`: ナレッジを論理的に分離するために使用する

同一remoteを持つ別cloneは同一`project_id`として扱う。

### 5.7 プロジェクトIDの明示上書き

自動解決では対応できないケース（ホスティング移行の予定がある、複数remoteの構成が特殊、など）のために、Git configによる明示上書きを許可する。

```bash
git config codeknowledge.projectId "github.com/company/order-api"
```

要件:

- `codeknowledge.projectId`が設定されている場合、5.3の自動解決より優先する。
- 設定値は5.3.2の正規化ルールを満たす形式であることを検証し、不正な形式（空文字、認証情報を含む、など）の場合はエラーとする。
- `resolve_project`の出力に、`project_id`の由来を示す`projectIdSource`（`config` / `remote` / `local`）を含める。

`codeknowledge.projectName`（表示名、5.2）とは独立した設定であり、片方のみの設定を許可する。

### 5.8 プロジェクトIDの変遷への対応

#### 5.8.1 想定される変遷ケース

`project_id`は不変ではなく、以下の操作により変化し得る。

| ケース | 変化の内容 |
|---|---|
| localリポジトリにremoteを追加 | `local:<hash>` → `<host>/<path>` |
| リポジトリのホスティング移行 | `<old-host>/<path>` → `<new-host>/<path>` |
| リポジトリ名・organization変更 | パス部分の変化 |
| localリポジトリのディレクトリ移動 | `local:<hash>`自体の変化 |

変遷が発生すると、旧`project_id`に紐づく既存ナレッジが検索対象外となる（ナレッジの孤立）。

備考（変遷のトリガー）:

- `project_id`の変化を引き起こすのは**remote設定の有無・内容**（`git remote add` / `set-url` / `remove`）であり、pushの実行有無ではない。remoteを追加した時点で、pushがまだ一度も行われていなくても、次回の`resolve_project`から新しい`project_id`が解決される。
- 変遷は`resolve_project`が実行されるまで検出されない。検出漏れを防ぐため、すべてのToolが処理冒頭でプロジェクト解決を実行する（5.9）ことにより、remote追加後の最初のTool呼び出しで必ず`project_id_changed`警告が返る。

#### 5.8.2 MVPでの要件

MVPでは自動的な再紐付けは行わない。ただし、孤立を無言で発生させてはならない。

1. `resolve_project`は、解決した`repository_root`が既存プロジェクトの`repository_root`と一致し、かつ`project_id`が異なる場合、これを検出する。
2. 検出時、出力に警告情報（`project_id_changed`、旧`project_id`、対象ナレッジ件数）を含める。
3. Agentはこの警告をユーザーへ通知し、必要に応じて移行を提案する。
4. 移行の実行手段として、保守用Tool `migrate_project`（10.9）を提供する。

#### 5.8.3 repository_rootの更新

`project_id`が同一で`repository_root`のみ異なる場合（同一remoteの別clone、cloneの移動）は正常系とする。`resolve_project`実行時に`projects.repository_root`を現在値へ更新する。これは5.6の役割分担に基づく。

### 5.9 解決処理の実行タイミング

- すべてのToolは、処理冒頭で`workingDirectory`からプロジェクト解決を実行する。Agentが渡す`projectId`類の値を信用しない（16章に準拠）。
- 同一プロセス内での解決結果は、`repository_root`と`git config`の変更検出を条件にキャッシュしてよい。ただしキャッシュ有効期間中もコミットハッシュは都度取得する（HEADはTool呼び出し間で頻繁に変化するため）。

---

## 6. ナレッジモデル

### 6.1 Knowledge

特定テーマを表す論理的なナレッジ本体。

例:

```text
注文完了メール仕様
```

主な項目:

- `id`
- `project_id`
- `canonical_key`
- `title`
- `current_version_id`
- `created_at`
- `updated_at`

`canonical_key`は同一テーマの重複作成を防ぐために使用する。

例:

```text
domain.mail.order-completed
```

#### canonical_keyの重複乱立防止

`canonical_key`はAgentが生成するため、同一テーマに対して微妙に異なるキーが乱立するリスクがある。これを抑止するため、以下を要件とする。

- `save_knowledge`は、同一プロジェクト内に類似の`canonical_key`または類似タイトルのKnowledgeが存在する場合、保存を実行したうえで出力に`similarKnowledge`警告（候補一覧）を含める。
- Agent行動ルール（11章）で、新規保存前に`search_knowledge`による既存確認を必須とする。
- 完全一致する`canonical_key`が既に存在する場合、新規Knowledge作成ではなく既存Knowledgeへの新バージョン追加として扱う。

### 6.2 KnowledgeVersion

特定コミット時点で確定した調査結果。

主な項目:

- `id`
- `knowledge_id`
- `commit_hash`
- `branch_name`（null許容。detached HEAD状態ではnullを保存する）
- `original_question`
- `summary`
- `facts`
- `inferences`
- `confidence`（調査全体の確信度。値域は6.2.1に従う）
- `created_at`
- `created_by`
- `retain`
- `retain_reason`

最新確定バージョンの判定は`Knowledge.current_version_id`のみを正とする。バージョン側に`is_current`等の重複フラグを持たせない（二重管理による不整合を防ぐため）。

#### 6.2.1 確信度（confidence）の定義

確信度は数値スコアではなく、**3段階の列挙値**とする。

| 値 | 判定基準 |
|---|---|
| `high` | 根拠コードを直接読み、複数の根拠（実装・呼び出し元・テスト等）が互いに整合している |
| `medium` | 主要な根拠コードは直接確認したが、周辺（呼び出し元、設定、テスト等）の確認が不足している |
| `low` | 命名・慣習・類似コードからの推測が主であり、直接のコード確認が不足している |

適用範囲:

- `KnowledgeVersion.confidence`: 調査全体としての確信度。
- 各inferenceの確信度: 個々の推論に対する確信度。

両者は同一の列挙型・同一の判定基準を使用し、スコープのみが異なる。

数値スコア（0.0〜1.0等）を採用しない理由:

- 確信度を付与するのはAI Agentであり、LLMが自己申告する数値確信度は較正されておらず、値の差分（例: 0.87と0.91）に意味を持たせられない。
- 本システムには複数の異なるAgent・モデルが書き込むため（2.1）、数値ではモデル間で基準が揃わない。運用的な判定基準に紐づく列挙値の方が、モデルを跨いだ一貫性とフィルタリング可能性を確保できる。

実装要件:

- SQLiteでは`TEXT`型＋`CHECK`制約（`high` / `medium` / `low`のみ許可）で保存する。
- MCP Tool入力のJSON Schemaでも`enum`として宣言し、定義外の値を含む`save_knowledge`は`invalid_arguments`エラーとして拒否する。
- `save_knowledge`のTool descriptionに上表の判定基準を記載し、Agentが一貫した基準で付与できるようにする。
- `get_knowledge`・`search_knowledge`の出力に確信度を含め、Agentが再検証要否の判断に使用できるようにする。

#### facts

コードから直接確認できた事実。

各事実は最低1件の根拠（Evidence）を参照しなければならない。この制約はApplication層の保存バリデーションで強制し、根拠を持たない事実を含む保存要求はエラーとして拒否する（SQLiteの宣言的制約では表現しないため、テストで担保する）。

事実（facts）には確信度を付与しない。根拠により直接確認済みであることが事実の定義であり、確信が持てない内容は事実ではなく推論（inferences）として保存する。

#### inferences

コードから推測した内容。

推論には以下を持つ。

- 推論本文
- 確信度（6.2.1の列挙値）
- 推論理由
- 関連する根拠

事実と推論を混在させない。

### 6.3 Evidence

調査結果の根拠となったコード位置。

主な項目:

- `id`
- `knowledge_version_id`
- `file_path`
- `symbol_id`
- `symbol_name`
- `symbol_kind`
- `signature`
- `start_line`
- `end_line`
- `commit_hash`
- `file_hash`
- `symbol_hash`
- `reason`

#### ファイルパスの正規化

`file_path`は以下の形式で保存する。

- Gitリポジトリルートからの相対パスとする。
- パス区切りは`/`へ統一する（Windowsの`\`を保存しない）。
- 大文字小文字はGitが認識しているパス表記を正とする。

これにより、鮮度検証時のGit diff結果との突合、および別clone・別OS環境での再利用を可能にする。

#### シンボル識別

メソッド名のみではなく、可能な限り完全修飾名およびシグネチャを保存する。

例:

```text
MyApp.Application.Orders.OrderService.CompleteAsync(
    OrderId,
    CancellationToken
)
```

#### 行番号の扱い

行番号は補助情報として保存するが、識別の主手段にしない。

優先順位:

1. 完全修飾シンボルID
2. シグネチャ
3. ファイルパス
4. シンボルハッシュ
5. 行番号

### 6.4 Relation

調査で判明したシンボル間の関係。

初期実装で許可する関係種別:

- `calls`
- `implements`
- `inherits`
- `reads`
- `writes`
- `publishes`
- `subscribes`
- `configured-by`
- `tested-by`

リポジトリ全体の完全な依存グラフは作成しない。調査で必要になった範囲のみ保存する。

### 6.5 KnowledgeDiff

2つのコミットまたは2つのナレッジバージョン間の意味的な差分。

主な項目:

- `id`
- `knowledge_id`
- `from_version_id`
- `to_version_id`
- `from_commit`
- `to_commit`
- `summary`
- `changes`
- `created_at`
- `created_by`

変更種別の例:

- `behavior_changed`
- `symbol_added`
- `symbol_removed`
- `symbol_moved`
- `signature_changed`
- `configuration_changed`
- `dependency_changed`
- `flow_changed`
- `no_behavioral_change`

---

## 7. 履歴保持要件

### 7.1 基本方針

すべての調査履歴を永続保存しない。

保持するのは以下とする。

#### 常に保持

- 各Knowledgeの最新確定バージョン

#### 条件付きで保持

- 保存済み`KnowledgeDiff`の比較元または比較先
- 本番リリース時点
- 障害発生時点
- 仕様変更前の基準点
- ユーザーが明示的に保存を指示したバージョン
- `retain = true`のバージョン

#### 削除可能

- 最新版ではない
- `KnowledgeDiff`から参照されていない
- `retain = false`
- 一時的な調査結果
- 保存指示がなかった比較結果

### 7.2 Gitとの責務分担

コード差分や完全な履歴はGitを正とする。

本システムは以下のみ保持する。

- 調査時点の意味的な理解
- 根拠コード
- 重要な仕様変更
- 保存価値がある比較結果

### 7.3 一時比較結果のライフサイクル

各MCPクライアントは別々のMCPサーバープロセスを起動するため（10.11参照）、一時比較結果はプロセスメモリではなくSQLite（`temporary_comparisons`テーブル）へ保存し、プロセスを跨いで参照可能とする。

要件:

1. 一時比較結果は`temporary_comparison_id`、`project_id`、`knowledge_id`、作成日時、比較内容を持つ。
2. `save_comparison`は`temporaryComparisonId`の明示指定を必須とする。「直前の結果」という暗黙参照はMCPサーバー側では解決しない（複数セッション並行時の誤保存を防ぐため。Agentは`compare_knowledge`の出力に含まれるIDを保持して指定する）。
3. 有効期限（TTL）は作成から24時間とする。期限切れの一時比較結果に対する`save_comparison`は`temporary_comparison_expired`エラーを返す。
4. 期限切れレコードは、MCPサーバープロセス起動時および`save_comparison`実行時に削除する（常駐クリーンアップ処理は持たない）。
5. 一時比較結果は7.1の保持ルールの対象外であり、保存指示がない限り永続化されない。

---

## 8. 検索要件

### 8.1 検索方式

SQLite FTS5を、内蔵の**trigramトークナイザ**で使用する。

```sql
CREATE VIRTUAL TABLE knowledge_fts USING fts5(
    ...,
    tokenize = "trigram"
);
```

採用理由と制約:

- FTS5のデフォルトトークナイザ（unicode61）はスペース区切り言語前提であり、日本語文字列を単語分割できないため、日本語ナレッジが実質検索不能になる。unicode61は採用しない。
- trigramはSQLite 3.34.0以降の内蔵トークナイザであり、外部拡張DLLなしで日本語を含む任意言語の部分一致検索に対応できる（2章の制約と両立する唯一の現実解）。
- trigramは3文字トークンのみをインデックスするため、**2文字以下のキーワードは`MATCH`でヒットしない**。この制約は8.3のハイブリッド検索で吸収する。
- 初期実装ではベクトル検索・形態素解析を使用しない。

### 8.2 検索対象

以下を検索対象とする（FTS5インデックスおよびLIKEフォールバックの両方）。

- タイトル
- 元の質問
- 概要
- 事実
- 推論
- タグ
- シンボル名
- 完全修飾シンボルID
- ファイルパス
- canonical key

インデックス対象は各Knowledgeの**最新確定バージョンのみ**とする（12.1参照）。

### 8.3 ハイブリッド検索（文字数による自動振り分け）

`search_knowledge`は、キーワードの文字数（Unicodeコードポイント単位）により検索ルートを自動的に振り分ける。Agentは内部差異を意識しない。

```text
1. プロジェクト解決（失敗時 git_repository_required）
2. キーワード正規化・サニタイズ（8.4）
3. 振り分け:
     3文字以上 → FTSルート
     1〜2文字   → LIKEルート
4. 両ルートの結果をマージ・ランキング
5. 上位 limit 件を返却
```

#### FTSルート（3文字以上）

- キーワードは**ANDではなくORで結合**する。Agentが展開するキーワードは網羅的であり、ANDでは絞り込み過多により0件になりやすい。多めに候補を拾い、ランキングで上位を選別する。
- `bm25()`で順位付けする（値が小さいほど関連度が高い）。
- prefixクエリ（`仕様*`）は採用しない。trigramのトークン構造上、語の出現位置により前方一致トークンが存在せず取りこぼすため、2文字以下はLIKEルートに一本化する。

#### LIKEルート（1〜2文字）

- 現在プロジェクトの最新確定バージョンの実テーブルを`LIKE '%keyword%'`で直接走査する。
- 対象カラムは以下とする: タイトル、元の質問、概要、事実、推論、タグ、シンボル名、canonical key。
- **ファイルパスはLIKEルートの対象外とする。** 1〜2文字の部分一致はパス中の頻出文字列（`er`、`api`の一部等）に高頻度でヒットし、ノイズが利得を上回るため。ファイルパスを狙う検索は3文字以上のキーワード（FTSルート）で行う。実測でこの判断に反する結果が出た場合は実装ノートに記録のうえ本書を改訂する。
- 性能根拠: 検索対象は最新確定バージョンのみで、想定規模（13.3）のKnowledge数百〜数千件では数ms〜数十msに収まる見込みであり、MVPでは許容する。

#### マージ・ランキング

| 順位 | 条件 | ソートキー |
|---|---|---|
| 1 | FTS・LIKE両方でヒット | bm25スコア昇順 |
| 2 | FTSのみヒット | bm25スコア昇順 |
| 3 | LIKEのみヒット | ヒットキーワード数降順 → 更新日時降順 |

返却する各件には`matchedRoute`（`fts` / `like` / `both`）と`matchedKeywords`を含め、Agentが信頼度判断に使えるようにする。

### 8.4 クエリサニタイズ

生のキーワードをSQL文字列へ連結してはならない（13.1・16章に準拠。すべてパラメータ化する）。加えて、FTSとLIKEで危険文字が異なるため、それぞれに対するエスケープを必須とする。

#### FTS用

FTS5のMATCH構文ではハイフンがNOT演算子、`" ( ) * :`等がメタ文字として解釈される。

1. キーワード内の`"`は`""`へ置換する。
2. キーワード全体をダブルクォートで囲み、フレーズとして扱う（例: `sui-memory` → `"sui-memory"`）。
3. クォート済みキーワードを` OR `で結合し、1つのMATCHパラメータとして渡す。

#### LIKE用

1. `\` → `\\`、`%` → `\%`、`_` → `\_`へ置換する。
2. `LIKE @pattern ESCAPE '\'`を必ず指定する。

#### 共通正規化

- Unicode正規化（NFKC）を適用し、全角半角の表記揺れを吸収する。
- 正規化後に空文字となったキーワードは破棄する。全キーワードが無効な場合は`invalid_arguments`エラーとする。
- FTSルートでキーワード単位の構文エラーが発生した場合、当該キーワードのみ除外して継続する。

### 8.5 Agent側キーワード展開の要件

trigramの検索精度はAgentが渡すキーワードの質に依存する。`search_knowledge`のTool descriptionおよび各Agentのルールファイル（11章）に以下を明記する。

```text
keywordsには以下を含めて網羅的に展開すること。

1. 質問文中の名詞・複合語（例: 「メール仕様」「注文完了メール」）
2. 単語の英語表記（例: メール → mail, email / 仕様 → spec, specification）
3. 関連しそうなシンボル名・クラス名の推測（例: EmailSender, OrderCompleted）
4. 2文字の重要語も含めてよい（サーバー側で自動的に部分一致検索される）

キーワードは3文字以上の複合語を優先的に含めること。
```

サーバー側の同義語辞書はMVPでは実装しない。表記揺れの吸収はAgentのキーワード展開に委ねる。

### 8.6 将来拡張

検索精度が実運用で不足した場合のみ、以下を追加可能とする。

- サーバー側同義語展開（設定ファイルによる日英技術用語辞書）
- EmbeddingのSQLite BLOB保存とC#でのcosine similarity計算、FTS5結果とのRRF統合
- Roslynによる正規化シンボルハッシュ（9.4参照）

初期実装では`sqlite-vec`、外部ベクトルDB、形態素解析拡張を使用しない。いずれの拡張もApplication層内部の差し替えで実現し、MCP ToolのI/O契約は変更しない。

---

## 9. 鮮度検証要件

### 9.1 ステータス

検証結果は以下のいずれかとする。

- `valid`
- `partially_stale`
- `stale`
- `unknown`

### 9.2 判定概要

#### valid

- 根拠シンボルが存在する
- シンボルハッシュが一致する
- 調査内容に影響する関連箇所に変更がない

#### partially_stale

- 一部の根拠または関連箇所のみ変更されている
- 変更箇所のみ再調査すればよい

#### stale

- 主要根拠シンボルが削除・大幅変更されている
- 処理フローが変更されている
- 過去ナレッジをそのまま利用できない

#### unknown

- シンボルを特定できない
- 比較対象コミットが存在しない
- Git履歴が不足している（shallow clone等）
- 検証処理で確定できない

#### ワーキングツリーの未コミット変更

鮮度検証はコミット間の比較であり、未コミットのローカル編集は検証対象に含まれない。「validと判定されたが目の前のコードは編集済み」という齟齬を防ぐため、以下を要件とする。

- `validate_knowledge`および`compare_knowledge`の出力に`isWorkingTreeDirty`（根拠ファイルに未コミット変更が存在するか）を含める。
- Agent行動ルール（11章）で、`isWorkingTreeDirty = true`の場合は該当ファイルを直接確認するよう指示する。

### 9.3 検証対象

必要に応じて以下を検証する。

- 実装メソッド
- 呼び出し元
- インターフェース
- DI登録
- 設定ファイル
- SQL
- テスト
- Feature Flag
- 外部API契約を表すコード

### 9.4 ハッシュ

最低限以下を持つ。

- 調査全体の`baseCommit`
- 根拠ファイルの`fileHash`
- 根拠シンボルの`symbolHash`

段階構成:

1. ファイルハッシュ
2. シンボル範囲のテキストハッシュ（空白正規化あり、言語非依存）
3. Roslyn構文木による正規化ハッシュ（C#ファイルのみ）

**Phase 2は段階2から開始する。** 段階1（ファイルハッシュのみ）は採用しない。ファイル単位の変更検知では、根拠と無関係な同一ファイル内の変更でもstale判定となり、`partially_stale`（変更箇所のみ再調査）という本システムの中核価値を提供できないため。

段階2の要件:

- `symbolHash`は、保存時点のコミットにおける根拠シンボルのテキスト範囲から計算する。
- ハッシュ計算前に空白の正規化（行末空白除去、改行コード統一、連続空白の縮約）を行い、フォーマット差分による誤検知を抑える。
- コメント変更は段階2では実質変更として検知される（`partially_stale`側に倒れる）。これは安全側の誤りとして許容し、コメント・空白を完全に無視する検証は段階3（Roslyn、Phase 4）で対応する。
- 検証時のシンボル位置の再特定にはGit diffの行マッピングを補助として使用してよいが、最終的な一致判定はハッシュ比較で行う（6.3の識別優先順位に準拠）。

### 9.5 アーキテクチャ要件

#### 9.5.1 基本方針

MVPはMCPインターフェースを提供するが、システム本体をMCP専用設計にしない。

以下の責務を分離する。

- Domain: ナレッジ、バージョン、差分、根拠などのドメインモデル
- Application: 検索、保存、検証、比較などのユースケース
- Infrastructure: SQLite、FTS5、Git、ハッシュ計算
- MCP: MCP Toolの入出力とApplication層への変換

#### 9.5.2 禁止事項

- MCP Toolハンドラーから直接SQLを実行しない
- MCP Toolハンドラーから直接Gitコマンドを実行しない
- MCP固有のDTOをDomainモデルとして使用しない
- Application層がMCP SDKへ依存しない
- 将来CLIから再利用できない形でロジックを実装しない

#### 9.5.3 将来CLI要件

将来のCLIはApplication層を直接呼び出せるものとする。

想定コマンド例:

```text
ck project resolve
ck knowledge search
ck knowledge get
ck knowledge validate
ck knowledge compare
ck knowledge save
ck knowledge save-comparison
```

CLI追加時に、検索・保存・検証・比較ロジックの再実装を必要としない構成であること。

### 9.6 Git操作の実装方式

Git操作は`git` CLIへのシェルアウトで実装する。ライブラリバインディング（LibGit2Sharp等）は採用しない。

理由:

- worktree・submoduleを含む「Gitコマンドが解決できる構成はすべて許可する」（3.1.1）という要件を、`git rev-parse`等の実コマンド委譲で確実に満たすため。
- `git config codeknowledge.*`の読み取り（5.2、5.7）にGitの設定解決順序（system/global/local/worktree）をそのまま利用するため。

要件:

- Git操作はInfrastructure層の`IGitRepository`（名称は実装時に決定）に集約し、コマンド文字列を他層へ漏らさない。
- `git`がPATH上に存在しない場合、`git_not_found`エラーを返し、すべての処理を中断する。
- Gitコマンドの標準出力・標準エラーはUTF-8として扱い、パスのエンコーディング問題（`core.quotepath`）を考慮する。
- コマンド実行にはタイムアウト（既定30秒）を設ける。
- ユーザー入力やAgent入力をコマンド引数として渡す場合、シェル経由ではなくプロセス引数配列として渡す（コマンドインジェクション防止）。

---

## 10. MCP Tool要件

### 10.1 `resolve_project`

現在の作業ディレクトリからプロジェクト情報を解決する。

入力例:

```json
{
  "workingDirectory": "C:\\work\\order-system"
}
```

出力例:

```json
{
  "projectId": "git.example.local/team/order-system",
  "projectIdSource": "remote",
  "displayName": "Order System",
  "repositoryRoot": "C:\\work\\order-system",
  "remoteUrl": "git.example.local/team/order-system",
  "currentCommit": "def456",
  "branchName": "main",
  "warnings": []
}
```

エラー条件:

- Gitリポジトリが見つからない
- Gitルートを解決できない
- 現在コミットを取得できない
- `git`コマンドが見つからない（`git_not_found`）

エラー例:

```json
{
  "code": "git_repository_required",
  "message": "The current directory is not inside a usable Git repository."
}
```

このエラー時は、プロジェクト登録、検索、保存、検証、比較を継続しない。

`project_id_changed`警告（5.8.2）の出力例:

```json
{
  "warnings": [
    {
      "code": "project_id_changed",
      "previousProjectId": "local:3fa2b8c1d4e5f607",
      "knowledgeCount": 12
    }
  ]
}
```

### 10.2 `search_knowledge`

現在のプロジェクト内から関連ナレッジをハイブリッド検索（8.3）で検索する。

入力例:

```json
{
  "workingDirectory": "C:\\work\\order-system",
  "query": "注文完了後のメール通知",
  "keywords": [
    "注文完了",
    "メール",
    "通知",
    "OrderCompleted",
    "Email"
  ],
  "limit": 10
}
```

出力例:

```json
{
  "projectId": "github.com/company/order-api",
  "results": [
    {
      "knowledgeId": "knowledge-001",
      "canonicalKey": "domain.mail.order-completed",
      "title": "注文完了メール仕様",
      "summary": "...",
      "commitHash": "abc123",
      "updatedAt": "2026-07-01T10:00:00Z",
      "matchedRoute": "both",
      "matchedKeywords": ["メール", "通知"]
    }
  ],
  "totalCandidates": 12
}
```

要件:

- `projectId`はMCP側で解決する
- Agentから渡された`projectId`を無条件に信用しない
- 原則として現在のプロジェクト以外を検索しない
- `limit`の既定値は10、上限は50とする
- ヒット0件は空の`results`を返す（エラーにしない）

### 10.3 `get_knowledge`

指定ナレッジの最新確定バージョンまたは指定バージョンを取得する。

入力例:

```json
{
  "knowledgeId": "knowledge-001",
  "versionId": "version-002"
}
```

`versionId`省略時は最新確定バージョンを返す。

### 10.4 `validate_knowledge`

保存済みナレッジを現在のHEADまたは指定コミットに対して検証する。

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
  "baseCommit": "abc123",
  "targetCommit": "def456",
  "isWorkingTreeDirty": false,
  "changedEvidence": [
    "SmtpEmailSender.SendAsync"
  ],
  "unchangedEvidence": [
    "OrderService.CompleteAsync"
  ],
  "missingEvidence": [],
  "recommendedAction": "reinspect_changed_symbols"
}
```

### 10.5 `compare_knowledge`

最新確定バージョンまたは指定バージョンと、任意コミットを比較する。

入力例:

```json
{
  "workingDirectory": "C:\\work\\order-system",
  "knowledgeId": "knowledge-001",
  "from": "last-investigated",
  "toCommit": "def456"
}
```

出力には以下を含める。

- 比較元コミット
- 比較先コミット
- 変更ファイル
- 変更シンボル
- 一次差分情報
- 再調査推奨箇所
- `isWorkingTreeDirty`
- 一時比較結果ID（`temporaryComparisonId`。有効期限は7.3に従う）

MCPはGit差分と保存済み根拠を返す。意味的な最終説明はAgentが生成する。

### 10.6 `save_knowledge`

新規ナレッジまたは新しいナレッジバージョンを保存する。

入力には以下を含める。

- `workingDirectory`
- `canonicalKey`
- `title`
- `originalQuestion`
- `summary`
- `facts`
- `inferences`
- `evidence`
- `relations`
- `commitHash`

要件:

- `projectId`、`remoteUrl`、`repositoryRoot`、`fileHash`、`symbolHash`、`createdAt`など、機械的に取得可能な情報はMCP側で補完する。Agentにハッシュ計算やプロジェクトID生成をさせない。
- 根拠を1件も参照しない事実（fact）が含まれる場合、保存全体を`fact_requires_evidence`エラーとして拒否する（6.2）。
- 完全一致する`canonicalKey`が既存の場合、既存Knowledgeへの新バージョン追加として処理する（6.1）。
- 類似する`canonical_key`またはタイトルが存在する場合、出力に`similarKnowledge`警告を含める（6.1）。
- `evidence.file_path`は保存時にリポジトリ相対・`/`区切りへ正規化する（6.3）。

### 10.7 `save_comparison`

一時比較結果を永続保存する。

入力例:

```json
{
  "temporaryComparisonId": "comparison-temp-001",
  "promoteToCurrent": true,
  "retainFromVersion": true
}
```

処理:

- `KnowledgeDiff`を保存
- 比較先の`KnowledgeVersion`を保存
- `promoteToCurrent = true`の場合、`Knowledge.current_version_id`を更新し、同一トランザクション内でFTSインデックスを更新（12.1）
- 比較元が差分から参照される場合は保持
- 期限切れの`temporaryComparisonId`は`temporary_comparison_expired`エラー（7.3）

### 10.8 `delete_orphan_versions`

参照されていない古いバージョンを削除する保守用Tool。

削除条件:

- 最新確定バージョンではない
- `KnowledgeDiff`から参照されていない
- `retain = false`
- 一時状態ではない

公開形態:

- **MCP Toolとして公開する**（MVPではCLIが存在しないため、MCP Tool以外に実行手段がない）。
- 自動実行はしない。ユーザーの明示指示があった場合のみAgentが呼び出す（11章）。
- Tool定義のアノテーションで破壊的操作であることを宣言し（`destructiveHint`）、クライアント側の実行確認UIが機能するようにする。
- 出力に削除対象の件数と一覧を含める。`dryRun: true`入力をサポートし、削除せずに対象一覧のみ返せること。

### 10.9 `migrate_project`

旧`project_id`配下のナレッジを新`project_id`へ付け替える保守用Tool（5.8）。

入力例:

```json
{
  "fromProjectId": "local:3fa2b8c1d4e5f607",
  "toProjectId": "github.com/company/order-api",
  "mergeStrategy": "fail_on_conflict"
}
```

要件:

- **MCP Toolとして公開する**（5.8の警告→ユーザー同意→移行のフローがAgentとの会話内で完結する必要があるため）。
- Tool定義のアノテーションで破壊的操作であることを宣言する（`destructiveHint`）。
- 付け替えは全件を単一トランザクションで実行する。
- 移行先に同一`canonical_key`のKnowledgeが既に存在する場合、MVPでは`fail_on_conflict`（エラーで中断）のみをサポートする。
- 移行はユーザーの明示指示があった場合のみ実行する。Agentが警告検出を理由に自動実行してはならない。
- 移行後、旧プロジェクトのレコードは`knowledge`が0件になった場合に削除する。

### 10.10 MCPクライアント設定例

MVPではWindows向けに発行した`CodeKnowledge.Mcp.exe`を、各MCPクライアントがstdioサーバーとして起動する。

以下の例では、実行ファイルを次の場所へ配置したものとする。

```text
C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe
```

同じSQLite DBを共有するため、MCPサーバー側の既定DBパスは以下とする。

```text
%LOCALAPPDATA%\CodeKnowledge\knowledge.db
```

各クライアントから同時に起動される可能性があるため、SQLiteではWAL、busy timeout、foreign keysを有効にする。

```sql
PRAGMA journal_mode = WAL;
PRAGMA busy_timeout = 5000;
PRAGMA foreign_keys = ON;
```

#### 10.10.1 Cursor

対象リポジトリに`.cursor/mcp.json`（ユーザー全体の場合は`~/.cursor/mcp.json`）を作成する。

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

要件:

- `command`にはEXEの絶対パスを指定する
- MCPサーバーはstdioで動作する
- MCPサーバーの標準出力はMCP通信専用とする
- 通常ログは標準エラー出力へ書き込む
- プロジェクト単位設定を使用する場合、設定ファイルをリポジトリへ含めるかは社内ポリシーに従う

#### 10.10.2 GitHub Copilot in VS Code

対象リポジトリに`.vscode/mcp.json`を作成する。

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

要件:

- GitHub CopilotのAgentモードからMCP Toolを利用する
- `.vscode/mcp.json`ではトップレベルキーに`servers`を使用する
- 必要に応じてVS Codeの`MCP: Add Server`コマンドから登録してもよい
- EnterpriseまたはOrganizationのMCPポリシーでローカルMCPサーバーが許可されていること
- 本MVPはローカルEXEを使用するため、VS Code上のローカルCopilot Agentを主対象とする

#### 10.10.3 Claude Code

CLIコマンドでの登録を推奨する。

```powershell
# ユーザー全体
claude mcp add --transport stdio --scope user code-knowledge -- "C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe"

# プロジェクト単位（リポジトリルートに .mcp.json が生成される）
claude mcp add --transport stdio --scope project code-knowledge -- "C:\Tools\CodeKnowledge\CodeKnowledge.Mcp.exe"
```

確認コマンド:

```powershell
claude mcp list
claude mcp get code-knowledge
```

要件:

- stdioサーバーのコマンドと引数は`--`より後ろへ指定する
- プロジェクト単位のMCPサーバーは初回利用時に承認を要求される場合がある
- `CLAUDE_PROJECT_DIR`等のClaude Code固有環境変数だけに依存せず、MCP rootsまたはTool入力の`workingDirectory`からもプロジェクトを解決できること

### 10.11 クライアント間の設定差異

MCPサーバー本体、Tool名、Tool Schema、SQLite DBは共通とする。クライアントごとの差異は設定ファイル形式のみとする。

| クライアント | 主な設定場所 | トップレベルキー |
|---|---|---|
| Cursor | `.cursor/mcp.json` または `~/.cursor/mcp.json` | `mcpServers` |
| GitHub Copilot in VS Code | `.vscode/mcp.json` またはユーザープロファイル | `servers` |
| Claude Code | `.mcp.json` または `~/.claude.json` | `mcpServers` |

設定上の注意:

- WindowsパスをJSONへ記載する場合、`\`を`\\`へエスケープする
- EXEの配置場所を変更した場合は全クライアントの設定を更新する
- stdoutへ通常ログを出力しない
- 複数クライアントが同時に起動することを前提とする（各クライアントは別々のMCPサーバープロセスを起動する可能性がある）
- クライアント固有の設定情報をDomain層またはApplication層へ持ち込まない

---

## 11. Agent行動ルール

MCPサーバーだけではAgentが必ず使用する保証がないため、各Agent向けの指示ファイルに以下を記載する。

```markdown
既存コードの仕様、構造、処理フロー、過去の調査内容について質問された場合、
コード全体を調査する前にCode Knowledge MCPを検索すること。

手順:

1. 現在のプロジェクトを解決する。
2. `search_knowledge`で関連ナレッジを検索する。
   keywordsは網羅的に展開する: 質問中の名詞・複合語、英語表記、
   推測されるシンボル名を含め、3文字以上の複合語を優先する。
   2文字の重要語も含めてよい（サーバー側で部分一致検索される）。
3. 関連ナレッジが存在する場合は`validate_knowledge`を実行する。
4. `valid`の場合、保存済みナレッジを主に利用し、必要最小限のコード確認のみ行う。
   ただし`isWorkingTreeDirty`がtrueの場合、該当ファイルは直接確認する。
5. `partially_stale`の場合、変更された根拠および関連箇所のみ再調査する。
6. `stale`の場合、通常のコード調査を行う。
7. `unknown`の場合、根拠コードを確認して判断する。
8. 保存済みナレッジなしで新規のコード調査を完了した場合、調査結果を
   ナレッジとして保存するかユーザーへ提案する。ユーザーの明示指示または
   提案への同意がある場合のみ`save_knowledge`を呼び出す。
   同意なしに自動保存しない。
9. 新しい調査結果を保存する前に、既存の類似ナレッジを検索で確認する。
   保存時は事実と推論を分離し、事実には必ず根拠を付ける。
10. 差分調査結果は、ユーザーが保存を指示した場合のみ永続保存する。
    保存には`compare_knowledge`が返した`temporaryComparisonId`を指定する。
11. `resolve_project`が`project_id_changed`警告を返した場合、ユーザーへ通知し、
    ユーザーの明示指示がある場合のみ`migrate_project`を実行する。
12. 確信度が`low`の推論を回答の根拠にする場合、該当コードを直接確認してから使用する。
13. 別プロジェクトのナレッジを検索または回答に使用しない。
```

---

## 12. データベース構成

最低限以下のテーブルを持つ。

- `projects`
- `knowledge`
- `knowledge_versions`
- `facts`
- `fact_evidence`（factとevidenceの多対多。事実の根拠必須制約の実現手段）
- `inferences`
- `inference_evidence`（推論と関連根拠の多対多）
- `evidence`
- `relations`
- `knowledge_diffs`
- `knowledge_diff_changes`
- `temporary_comparisons`
- `knowledge_fts`

### 12.1 FTS同期

`knowledge_fts`には最新確定バージョンのみをインデックスする。

同期はトリガーではなく、**バージョン昇格処理と同一トランザクション内での明示的なdelete + insert**で行う。

```text
save_knowledge / save_comparison（promoteToCurrent = true）:

BEGIN TRANSACTION
    1. knowledge_versions へ新バージョンをINSERT
    2. knowledge.current_version_id を更新
    3. knowledge_fts から当該knowledgeの旧行をDELETE
    4. knowledge_fts へ新バージョン内容をINSERT
COMMIT
```

トリガーを採用しない理由: インデックス対象が「最新確定バージョンのみ」であり、`knowledge_versions`へのINSERTが常にFTS更新を意味しない（昇格を伴わない保存があり得る）ため、同期条件はApplication層のユースケースで制御する方が要件に素直で、テストも容易である。

過去バージョンも検索対象にする場合は、明示的な履歴検索モードとして分離する。通常検索で古いナレッジが上位表示されないようにする。

### 12.2 スキーママイグレーション

DBファイルは全プロジェクト・全クライアントで共有され、EXEのバージョンアップと独立して存在し続けるため、スキーマの版管理を必須とする。

- スキーマバージョンは`PRAGMA user_version`で管理する。
- MCPサーバーは起動時に`user_version`を確認し、自身が要求するバージョンより古い場合、前方向マイグレーションをトランザクション内で順次適用する。
- ダウングレード（新しいスキーマを古いEXEで開く）はサポートしない。`user_version`が自身の対応範囲を超える場合、`schema_version_unsupported`エラーを返し、DBを変更しない。
- マイグレーション適用前に、DBファイルのバックアップコピー（`knowledge.db.bak-<version>`）を同ディレクトリへ作成する。
- 複数プロセスの同時起動下でもマイグレーションが競合しないこと（排他トランザクションで直列化する）。

---

## 13. 非機能要件

### 13.1 セキュリティ

- 初期実装では外部ネットワーク通信を行わない
- ローカルGitリポジトリとローカルSQLiteのみを使用する
- 外部LLMやEmbedding APIへコードやナレッジを送信する機能はMCPサーバーには実装しない
- 秘密情報をナレッジへ保存しない
- 設定ファイルを根拠にする場合、パスおよび説明のみ保存し、パスワード、トークン、接続文字列などの値は保存しない
- remote URLに含まれる認証情報を`project_id`・`remote_url`へ保存しない（5.3.2）
- SQLパラメータを使用し、文字列連結でクエリを構築しない（FTS/LIKEのエスケープ要件は8.4）
- 外部プロセス起動は`git`のみとし、引数はプロセス引数配列で渡す（9.6）

### 13.2 可搬性

- Windows 11で動作する
- 管理者権限を必要としない
- DBサーバーを必要としない
- .NET 10ランタイムが導入済みであることを前提とする（2.2）
- SQLite DBファイルのみでデータを保持する

### 13.3 性能

想定規模:

- プロジェクト数: 数件から数十件
- Knowledge数: 数百から数千件
- KnowledgeVersion数: 数千件程度
- 検索頻度: Agent操作時に数回

初期実装ではFTS5＋LIKEフォールバックで十分な性能を目標とする（LIKEルートの性能根拠は8.3）。

### 13.4 信頼性

- 保存処理はトランザクションで実行する
- `KnowledgeVersion`保存、`current_version_id`更新、FTSインデックス更新は同一トランザクションで行う
- `KnowledgeDiff`保存と参照バージョン保持も同一トランザクションで行う
- スキーママイグレーションはトランザクションで実行し、適用前バックアップを作成する（12.2）
- Gitコマンド失敗時は状態を更新しない
- 比較対象コミットが存在しない場合は明確なエラーを返す
- 複数プロセスからの同時アクセスを前提とし、WAL＋busy_timeoutで直列化する。busy timeout超過時は明確なエラーを返す

### 13.5 ログ

以下を構造化ログとして記録する。

- Tool名
- project_id
- knowledge_id
- 比較元コミット
- 比較先コミット
- 検索件数
- 検証結果
- 保存結果
- エラー種別

要件:

- ログの出力先は標準エラー出力またはファイルとする。**標準出力へは一切のログを出力しない**（stdioのMCP通信を破壊するため）。
- ログフレームワーク（Microsoft.Extensions.Logging等）を使用する場合、コンソールロガーの既定出力先がstdoutである点に注意し、stderrへの出力を明示的に構成することを実装要件とする。
- 秘密情報やコード本文はログに出力しない。

---

## 14. 実装優先順位

### Phase 0: 前提検証（spike）

設計の前提となる技術要素を、本実装着手前に最小コードで確認する。

- [x] `Microsoft.Data.Sqlite`（SQLitePCLRaw bundle）同梱のSQLiteでFTS5が有効であること（SQLite 3.50.4で確認）
- [x] 同SQLiteのバージョンが3.34.0以降であり、`tokenize = "trigram"`の仮想テーブルが作成できること
- [x] 日本語3文字キーワードのMATCH、2文字キーワードのLIKEフォールバックが期待通り動作すること（「メール」「仕様」「確認」を確認ケースとする）
- [x] WAL＋busy_timeout設定下で、複数プロセスからの同時検索・保存がエラーなく動作すること
- [x] MCP C# SDK（stdio）でTool呼び出しが3クライアント（Cursor / Copilot / Claude Code）から成立すること（Claude Code 2.1.207で実機確認。Cursor / Copilotは2026-07-11のユーザー判断で検証対象外。`spikes/phase0/README.md`のDeviations参照）
- [x] framework-dependent単一ファイル発行のEXEが対象マシンで動作すること（ネイティブSQLiteライブラリの同梱確認を含む。EXE単体で動作、隣接ネイティブファイルなし）

検証結果は実装ノートに記録し、前提が崩れた場合は本要件定義へフィードバックする。

**Phase 0は2026-07-11に完了し、ユーザーがPhase 1への移行を承認した。検証記録は`spikes/phase0/README.md`を参照。**

### Phase 1: 最小実用版

- [x] Domain / Application / Infrastructure / MCPの責務分離
- [x] Gitリポジトリ解決（CLIシェルアウト、9.6）
- [x] Project ID生成（正規化ルール、5.3）
- [x] SQLiteスキーマ＋マイグレーション基盤（12.2）
- [x] FTS5（trigram）＋ハイブリッド検索（8章）
- [x] `resolve_project`
- [x] `search_knowledge`
- [x] `get_knowledge`
- [x] `save_knowledge`（fact根拠必須バリデーション含む）
- [x] 最新確定バージョンのみ管理
- [x] Agent向け利用ルール
- [x] 将来CLIからApplication層を再利用できる構成

**Phase 1は2026-07-13に全自動テスト、発行済みEXEのE2E、Claude Codeからの4 Tool実機検証、ドキュメント整備を完了し、ユーザーが完了を承認した。検証記録は`README.md`を参照。**

### Phase 2: 鮮度検証

**このPhaseで便利になること:** 保存済みナレッジが現在のコードでも信用できるかを判定できる。コード全体を毎回調べ直す代わりに、変更されていない根拠は再利用し、変更された根拠だけを再調査できる。根拠ファイルに未コミット変更がある場合も検知し、保存済みの判定を過信することを防ぐ。

**利用イメージ:** Agentが過去ナレッジを検索した後に`validate_knowledge`を実行する。`valid`なら必要最小限の確認で回答し、`partially_stale`なら変更箇所だけを調べ直し、`stale`または`unknown`なら実コードを再調査する。

- [x] `validate_knowledge`
- [x] ファイルハッシュ
- [x] シンボルハッシュ
- [x] Git diff
- [x] `valid / partially_stale / stale / unknown`判定
- [x] `isWorkingTreeDirty`

**Phase 2は2026-07-14に全自動テスト（218件）、`win-x64` / `osx-arm64`の発行、published-server E2E、ドキュメント整備を完了し、ユーザーが完了と`main`へのマージを承認した。実装・検証手順は`docs/superpowers/plans/2026-07-13-code-knowledge-phase2.md`、利用手順は`README.md`を参照。**

### Phase 3: 差分保存

**このPhaseで便利になること:** 過去の調査時点から何が変わったかを比較し、その変更内容を後の調査で再利用できる履歴として残せる。ナレッジが現在の内容になった経緯を追跡できるほか、不要になった旧バージョンの整理やProject ID変更時のナレッジ移行も行える。

**利用イメージ:** 仕様変更後にAgentが`compare_knowledge`で保存時点と現在を比較して変更内容を説明する。ユーザーが保存に同意した場合だけ、`save_comparison`で差分と更新後のナレッジを永続化する。

- `compare_knowledge`
- 一時比較結果（ライフサイクル管理、7.3）
- `save_comparison`
- `KnowledgeDiff`
- バージョン保持ルール
- orphan version削除
- `migrate_project`

### Phase 4: 検索強化

必要になった場合のみ実施する。

**このPhaseで便利になること:** 保存時とは異なる表記や言い回しの質問でも、意味的に近いナレッジを見つけやすくなる。またC#コードでは、整形やコメント変更など処理の意味に影響しない変更によって、ナレッジを古いと誤判定するケースを減らせる。

**利用イメージ:** 利用者が保存時とは異なる用語で質問しても、同義語展開と意味検索によって関連ナレッジが候補に含まれる。実運用でPhase 1〜3の検索精度または鮮度判定精度が不足した場合にのみ導入する。

- サーバー側同義語展開
- Embedding保存＋C#でのcosine similarity計算
- FTS5とのハイブリッド（RRF）検索
- Roslynによる正規化シンボルハッシュ

---

## 15. 受け入れ条件

### AC-01

異なる2つのGitリポジトリに同名のナレッジが存在しても、現在のリポジトリに属する結果のみ返される。

### AC-02

保存済みナレッジがある質問に対し、Agentがコード全体を再調査せずに関連ナレッジを取得できる。

### AC-03

保存済みナレッジの根拠ファイルおよび根拠シンボルを取得できる。

### AC-04

前回調査コミットと現在のHEADを比較し、変更された根拠と変更されていない根拠を区別できる。

### AC-05

ユーザー指定コミットを比較先として差分調査できる。

### AC-06

差分調査結果は、保存指示がない限り永続化されない。

### AC-07

保存指示があった場合、差分、新バージョン、最新バージョン更新、FTSインデックス更新がトランザクションで保存される。

### AC-08

古いバージョンでも、保存済み差分から参照されている場合は削除されない。

### AC-09

事実には根拠が必須であり、推論と区別して保存される。根拠を持たない事実を含む保存要求はエラーとして拒否される。

### AC-10

外部DBサーバー、Docker、管理者権限なしで、Windows 11上の.NET 10ランタイムを使用して動作する。

### AC-11

MCP Toolハンドラーを介さず、Application層のユースケースを単体テストから直接実行できる。

### AC-12

将来CLIプロジェクトを追加する際、検索・保存・検証・比較ロジックの再実装を必要としない。

### AC-13

Gitリポジトリ外でToolを実行した場合、`git_repository_required`エラーを返し、SQLiteへプロジェクトまたはナレッジを保存しない。

### AC-14

現在コミットを取得できないリポジトリでは、差分比較およびナレッジ保存を実行しない。

### AC-15

同一リポジトリをSSH形式とHTTPS形式のremote URLでそれぞれcloneした場合、両方のcloneで同一の`project_id`が解決され、ナレッジが共有される。

### AC-16

remote URLに認証情報が含まれる場合でも、`project_id`および保存される`remote_url`に認証情報が含まれない。

### AC-17

remoteを持たないリポジトリで作成したナレッジは、同一パスで実行する限り常に同一の`project_id`に紐づく。

### AC-18

localプロジェクトにremoteを追加した後の`resolve_project`は`project_id_changed`警告を返し、既存ナレッジは`migrate_project`の明示実行によってのみ新`project_id`へ移行される。

### AC-19

`codeknowledge.projectId`が設定されている場合、remote構成に関わらず設定値が`project_id`として使用される。

### AC-20

「メール 仕様 確認」のようなキーワード群で検索した場合、3文字語（FTS）と2文字語（LIKE）の両方がヒット判定に寄与し、「注文完了メール仕様」ナレッジが検索結果に含まれる。

### AC-21

ハイフンを含むキーワード（例: `sui-memory`）がFTS5のNOT演算子として誤解釈されず、`%`・`_`を含むキーワードがLIKEワイルドカードとして誤解釈されない。

### AC-22

キーワード間はOR挙動であり、1語のみ一致するナレッジも結果に含まれ、複数語一致が上位に順位付けされる。

### AC-23

検索は現在プロジェクトの最新確定バージョンのみを対象とし、旧バージョンが通常検索の結果に混入しない。

### AC-24

古いスキーマバージョンのDBファイルは起動時に自動マイグレーションされ、適用前バックアップが作成される。新しいスキーマのDBを古いEXEで開いた場合は`schema_version_unsupported`エラーとなり、DBは変更されない。

### AC-25

根拠ファイルに未コミット変更がある状態で`validate_knowledge`を実行すると、`isWorkingTreeDirty = true`が返る。

### AC-26

有効期限切れの一時比較結果に対する`save_comparison`は`temporary_comparison_expired`エラーを返し、何も永続化しない。

### AC-27

保存済みナレッジが存在しないテーマの新規調査結果を、`save_knowledge`により新規Knowledgeとして保存でき、以降のUC-01（再利用）の対象となる。保存はユーザーの明示指示または保存提案への同意を経た場合のみ行われる（提案・同意の確認はAgent行動ルールで担保し、MCPサーバーは同意有無を検証しない）。

### AC-28

確信度に`high` / `medium` / `low`以外の値（数値、未定義文字列、空文字）を指定した`save_knowledge`は`invalid_arguments`エラーとして拒否され、何も保存されない。

---

## 16. 実装上の禁止事項

- Agentが指定した`projectId`だけを信用して検索しない
- プロジェクト条件なしでナレッジを検索しない
- remote URLの正規化を行わずに`project_id`を生成しない
- `project_id`または`remote_url`へ認証情報を含めない
- 複数remote環境で非決定的な`project_id`を生成しない
- `project_id`の不一致検出時に、ユーザーの明示指示なくナレッジを自動移行しない
- 行番号だけで根拠を識別しない
- 過去ナレッジを検証せず現在も正しいと断定しない
- 推論を事実として保存しない
- 根拠を参照しない事実を保存しない
- すべての比較結果を自動永続化しない
- 最新版更新時に旧版を無条件削除しない
- Git履歴をSQLiteへ複製しない
- 初期実装で外部ベクトルDB・外部SQLite拡張DLLを導入しない
- 秘密情報や認証情報の値を保存しない
- MCP Toolハンドラーへ業務ロジック、SQL、Git操作を直接実装しない
- Gitリポジトリ外のフォルダをローカルプロジェクトとして自動登録しない
- コミットハッシュを取得できない状態でナレッジを保存しない
- 生のキーワードをFTS5 MATCH式またはLIKEパターンへエスケープなしで埋め込まない
- 標準出力へMCP通信以外の出力（ログ等）を行わない

---

## 17. 最終的な期待動作

ユーザー:

> 注文完了メールってどうなってたっけ？

Agent:

1. 現在のプロジェクトを解決
2. 過去ナレッジを検索（キーワードを日英・複合語で展開）
3. 鮮度を確認（未コミット変更の有無を含む）
4. 有効なら保存済みナレッジを元に説明
5. 必要箇所のみコード確認

ユーザー:

> 前に調べたときから今も同じ？

Agent:

1. 前回調査コミットを取得
2. 現在のHEADと比較
3. 変更箇所だけ再調査
4. 変更点と現在仕様を説明

ユーザー:

> その差分も保存しといて

Agent:

1. `temporaryComparisonId`を指定して一時比較結果を保存
2. 比較先コミット時点のナレッジバージョンを保存
3. 最新確定バージョンを更新（FTSインデックスも同時更新）
4. 比較元バージョンを差分参照のため保持

この一連の動作を、別プロジェクトのナレッジを混在させずに実現すること。

---

## 18. 決定済み事項（旧・未決事項）

本要件定義に未決事項は存在しない。検討過程で未決としていた論点は、以下の通りすべて決定済みである（実装計画の作成をブロックしないこと、および実装時の不備検出の基準とすることを目的に、本改訂で確定した）。

| # | 論点 | 決定 | 反映箇所 |
|---|---|---|---|
| 1 | LIKEルートの対象カラム | ファイルパスを対象外とする。1〜2文字の部分一致はパス中の頻出文字列へのノイズヒットが利得を上回るため。実測で判断が覆る場合は本書を改訂する | 8.3 |
| 2 | confidenceの値域 | 3段階列挙値（`high` / `medium` / `low`）。数値スコアはLLMの較正不能性とモデル間の基準不一致のため不採用 | 6.2.1 |
| 3 | シンボルハッシュの開始段階 | 段階2（シンボル範囲のテキストハッシュ、空白正規化あり）から開始。段階1はpartially_stale判定の粒度が粗すぎるため不採用。段階3（Roslyn）はPhase 4 | 9.4 |
| 4 | 保守用ToolのMCP公開 | `delete_orphan_versions`・`migrate_project`ともにMCP Toolとして公開（MVPではCLIが存在せず他に実行手段がないため）。破壊的操作アノテーションを付与し、ユーザー明示指示時のみ実行 | 10.8, 10.9 |
| 5 | 配布形態 | framework-dependent単一ファイル発行で確定。対象マシンの.NET 10ランタイムは導入済み確認済み。self-contained切り替えはスコープ外（ビルド構成変更のみで対応可能なことを要件化） | 2.2 |

実装中に本書と実装の乖離が発生した場合は、乖離内容を実装ノートの「Deviations」に記録し、本書へフィードバックする。
