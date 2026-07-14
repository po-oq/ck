# CodeKnowledge CLIアダプター設計

**日付:** 2026-07-14

**状態:** ドラフト（レビュー待ち）

**対象:** Phase 2完了後、MCPと並ぶ第2アダプターとしてのCLI追加

## 1. 目的

CodeKnowledgeの機能を、MCPに加えて**CLI（`CodeKnowledge.Cli`）**からも利用できるようにする。

背景として、社内環境のCursorではMCPサーバーが権限ポリシーで停止させられ利用できないケースがある。一方、Agentによる**ターミナルコマンド実行は許可されている**。この前提のもとで、AgentがCLIを「シェル経由で叩く道具」として利用し、MCPと同じくナレッジの検索・保存・取得・検証を行えるようにする。

CLIはMCPと**同じApplication層・同じ入出力スキーマ**を用いる。ロジックを二重化せず、挙動差ゼロを目標とする。要件定義書1.1章が想定する「本体ロジックはCore/Applicationに置き、MCPは薄いアダプター。CLIは将来追加する別アダプター」という構成を、実際に実現するものである。

## 2. 対応範囲

### スコープ内
- `CodeKnowledge.Cli`（新規アダプタープロジェクト）
- Phase 2で提供済みの5 Toolに対応するサブコマンド:
  `resolve` / `search` / `get` / `save` / `validate`
- JSON in / JSON out の入出力コントラクト
- 4環境向けのAgentルールファイル・テンプレート（README掲載）:
  Cursor / Claude Code / GitHub Copilot in VS Code / GitHub Copilot in Visual Studio
- Windows 11 x64向けの発行

### スコープ外
- Core / Application / Infrastructure層の変更（既存を流用するのみ）
- MCPアダプターの挙動変更
- Windows以外のOS（Mac等）向けCLI発行 ※CLIはWindows 11中心の利用前提
- Phase 3以降のTool（compare_knowledge、migrate_project等）のCLI化
- ルールファイルの利用先リポジトリへの自動設置（テンプレ提供にとどめる）

## 3. アーキテクチャ

Core / Application層は変更せず、CLIを第2アダプターとして追加する。MCPと同じApplicationサービス・同じDTOを呼ぶ。

```
CodeKnowledge.Core / Application  ← 変更なし（既存）
CodeKnowledge.Infrastructure      ← 変更なし（既存）
CodeKnowledge.Mcp                 ← 既存アダプター（変更なし）
CodeKnowledge.Cli                 ← 新規アダプター（本設計）
```

CLIは以下の責務のみを持つ薄いアダプターとする。

- サブコマンド・オプションのパース
- 入力JSONの読み取り（stdin または `--input`）とDTOへのデシリアライズ
- Applicationサービスの呼び出し
- 結果DTOのJSONシリアライズとstdoutへの出力
- 例外・バリデーション失敗の終了コードへのマッピング
- ログ・進捗のstderrへの出力

ビジネスロジック、SQLiteアクセス、Git操作、検索、鮮度検証をCLIプロジェクトへ直接実装してはならない（要件1.1）。

## 4. コマンド表面

```
code-knowledge resolve
code-knowledge search
code-knowledge get
code-knowledge save
code-knowledge validate
```

- 各サブコマンドは、対応するMCP Toolと**同一の入力スキーマ・出力スキーマ**を持つ。
- `workingDirectory` はカレントディレクトリから自動解決する。`--cwd <path>` で明示上書きできる。
- 各サブコマンドの `--help` は、**入力JSONスキーマと最小例**を出力する。Agentがルールファイルに頼らず正しい入力形を自力で引けるようにするため。
- `code-knowledge --help` はサブコマンド一覧を出力する。

## 5. 入出力コントラクト

### 入力
- 既定は **stdinにJSON**を渡す。
- 併せて **`--input <file.json>`** を提供する。Windowsのpowershell/cmdでは`echo`によるインラインJSONのクォート処理が壊れやすいため、Agentは一時ファイルへJSONを書いてパスを渡す方式を選べる。
- stdinと`--input`が両方指定された場合は`--input`を優先する。

### 出力
- **stdoutにはJSONのみ**を出力する。構造はMCP Toolの結果DTOと同一とする。
- ログ・進捗・診断メッセージは**stderr**へ出力する（MCP実装と同じ流儀。stdoutをAgentがそのままパースできる状態に保つ）。

### 改行を含む多行データの扱い（必須要件）
`save`の`summary`・`facts`・根拠に付随するコード片などは、改行を含む多行テキストになる。これらは**JSON文字列値の中で改行を`\n`にエスケープして運ぶ**。JSONのパース段階で元の改行へ復元され、DBには多行のまま保存される。フラグ引数方式ではシェルのクォート/改行処理で壊れるが、JSON in方式ではこの問題が原理的に発生しない。

多行かつ大きな`save`入力では、シェルのインラインJSON（`echo '{...}'`）を避け、**Agentが一時ファイルへJSONを書いて`--input <file.json>`で渡す方式を推奨ルートとする**。これによりシェルのクォート処理を一切経由せず、改行を含むデータを確実に登録できる。CLIから多行ナレッジを登録できることは本設計の必須要件であり、テスト（8章）で明示的に検証する。

### 終了コード
| コード | 意味 |
|---|---|
| `0` | 成功 |
| `1` | 入力不正・バリデーション失敗（JSON不正、必須項目欠落、根拠なし保存など） |
| `2` | 前提エラー（Gitリポジトリ未解決、DB接続不可など実行不能な状態） |

エラー時はstderrに人間可読なメッセージを出し、必要に応じてstdoutに機械可読なエラーJSONを出力する。Agentが終了コードで成否を判定できることを最優先とする。

## 6. Agent連携（ルールファイル）

CLIはMCPと異なりAgentのTool一覧へ自動登場しない。そのため、コマンドの存在・利用タイミング・呼び出し方をAgentへ教えるルールファイルが必要となる。これは要件2.1章がMCPについても求めている「AgentへTool利用を促すルールファイル」と同じレバーをCLIへ適用するものである。

対象4環境のルールファイル実体は3種である（Copilotの2環境は同一ファイルを読む）。

| 環境 | ルールファイル |
|---|---|
| Cursor | `.cursor/rules/code-knowledge.mdc` |
| Claude Code | `CLAUDE.md` |
| GitHub Copilot in VS Code / Visual Studio | `.github/copilot-instructions.md` |

これらは**利用先リポジトリ側に置く**ものであり、`ck`リポジトリには自動設置しない。成果物としてはREADMEに**共通のテンプレート断片を1つ**掲載し、利用者が上記3ファイルへ転記する。テンプレートには最低限以下を含める。

- 既存機能を調べる際は、まず `search` で既存ナレッジを検索してから回答する
- 調査完了後、**ユーザーの明示指示または保存提案への同意後**に `save` する（要件のAgent行動ルールに整合）
- 入力スキーマが不明な場合は各サブコマンドの `--help` を実行して確認する
- 入力はstdin、または一時ファイル＋`--input`で渡す

Agentのモデル性能・Tool選択方針により自動呼び出しの確実性は異なる（要件2.1に同旨）。本設計はその確実性を保証するものではなく、利用を促す仕組みを提供する。

## 7. 配布・DB共有

- MCPと同じ **framework-dependent 単一ファイル**として発行する。対象は **win-x64のみ**。
  ```
  dotnet publish src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj \
    --configuration Release --runtime win-x64 --self-contained false \
    --output artifacts/cli/win-x64
  → artifacts\cli\win-x64\CodeKnowledge.Cli.exe
  ```
- 利用者側には.NET 10 Runtime x64が必要（MCPと同条件）。
- **DB共有**: CLIとMCPは同じ`knowledge.db`を共有できる。両アダプターに同じ`CODEKNOWLEDGE_DB_PATH`を設定すれば、MCPで保存したナレッジをCLIから検索でき、その逆も成立する。既定パス（実行ファイル隣の`knowledge.db`）のままだとMCPとCLIで別DBになるため、共有したい場合は同一パス指定が必要である旨をREADMEで案内する。
- SQLite同時アクセスは既存のWAL＋busy timeout設定に従う（MCPとCLIが同一DBを同時に開いても既存設定の範囲で扱う）。

## 8. テスト

Application層は既存テストで担保済みのため、CLIテストは**アダプター固有の関心**に限定する。

- サブコマンド・オプションのパース
- stdin入力と`--input <file>`入力の両系統の読み取り、両方指定時の`--input`優先
- **改行を含む多行データの`save`が正しく登録され、`get`で改行を保ったまま取得できること**（stdin経由・`--input`経由の両方）
- 出力JSONの構造がMCP結果DTOと一致すること
- 終了コード（`0` / `1` / `2`）のマッピング
- Gitリポジトリ未解決時にstderr＋非0で失敗すること

加えて、**同一入力に対しMCPアダプターとCLIアダプターが同一の出力JSONを返すこと**を突き合わせる整合テストを設け、挙動差ゼロを担保する。

## 9. 未決事項

現時点で未決事項はない。実装計画作成時に、既存Phase 2の`CodeKnowledge.Mcp`プロジェクト構成・DTO定義・テストfixtureを参照し、CLIプロジェクトの雛形と整合テストの具体を確定する。
