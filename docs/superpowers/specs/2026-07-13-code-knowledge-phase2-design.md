# Code Knowledge Phase 2 鮮度検証 設計書

要件定義書: `docs/code-knowledge-tool-requirements-v2.md`（以下「要件」）
前提: Phase 1完了（`docs/superpowers/specs/2026-07-11-code-knowledge-phase1-design.md`、要件14章）

## 1. 目的

Phase 2では、保存済みナレッジの最新確定バージョンを現在のHEADまたは指定コミットに対して検証し、根拠ごとの鮮度とナレッジ全体の鮮度を返す。

利用者とAgentは、変更されていない根拠を再利用し、変更・削除・判定不能になった根拠だけを直接確認できる。根拠ファイルに未コミット変更がある場合は、コミット間では有効でも手元のコードが異なる可能性を明示する。

Phase 2で提供するもの:

- MCP Tool `validate_knowledge`
- `valid` / `partially_stale` / `stale` / `unknown` の全体判定
- Evidence単位の `unchanged` / `changed` / `missing` / `unknown` 判定
- Git diffによるrenameと行移動の追跡
- ファイルハッシュとシンボル範囲ハッシュによる検証
- Evidence対象ファイルの未コミット変更検知
- Agent向け利用ルールとREADMEの更新

## 2. スコープ

### 2.1 対象

- 最新確定バージョンの保存コミットをbaseとする検証
- `targetCommit`省略時の現在HEAD検証
- 任意のローカルGitコミットをtargetとする検証
- 同一ファイル内で行位置だけが変わったシンボルの再特定
- 内容を維持したファイルrenameまたは移動の追跡
- Phase 1で`symbol_hash`が保存されなかったEvidenceの安全なフォールバック
- staged、unstaged、削除、renameを含むworking tree dirty検知

### 2.2 対象外

- `compare_knowledge`
- 一時比較結果、`save_comparison`、`KnowledgeDiff`
- 過去ナレッジバージョンを明示指定した検証
- ナレッジの自動更新または自動保存
- DBスキーマ変更
- Roslynまたは言語固有パーサーによるシンボル解析
- シンボル名やシグネチャが変わった後の意味的同一性判定
- リポジトリ全体を走査するシンボルインデックス

上記はPhase 3またはPhase 4の範囲とする。

## 3. 採用方式

Git diff、rename検出、保存済みハッシュ、対象ファイル内のシンボル範囲ハッシュ再探索を組み合わせる。

1. Git diffでbaseからtargetへのパス変更と行対応を得る。
2. ファイル全体のハッシュが一致すればEvidenceを`unchanged`とする。
3. ファイルが変わっていても、保存時のシンボル範囲と同じハッシュをtargetファイル内で再特定できれば`unchanged`とする。
4. シンボル範囲を再特定できなければ`changed`とする。

この方式は言語非依存であり、同一ファイル内の無関係な変更だけで根拠を陳腐化させない。完全修飾名を構文解析して追跡する方式はPhase 4へ残す。

### 3.1 不採用方式

#### ファイルハッシュのみ

実装は単純だが、同じファイルの無関係な変更でもEvidenceを変更扱いにする。要件9.4が不採用としている段階1に相当し、変更箇所だけを再調査するPhase 2の価値を満たさない。

#### Roslynによるシンボル解析

完全修飾シンボルIDやシグネチャから再特定しやすいが、C#専用になり、依存関係と実装規模が大きい。要件9.4の段階3およびPhase 4の範囲なので採用しない。

## 4. アーキテクチャ

既存の依存方向 `CodeKnowledge.Mcp → CodeKnowledge.Core ← CodeKnowledge.Infrastructure` を維持する。

### 4.1 Core

`ValidateKnowledgeUseCase`を追加する。責務は以下とする。

1. `ResolveProjectUseCase`を使ったプロジェクト解決
2. `IKnowledgeStore.GetDetail`による最新確定バージョン取得
3. baseとtargetの解決
4. Git比較情報とファイル内容の取得
5. Evidence単位の検証
6. 全体ステータスと推奨アクションの集約

Coreへ以下のドメイン型を追加する。

- `ValidationStatus`: `Valid` / `PartiallyStale` / `Stale` / `Unknown`
- `EvidenceValidationStatus`: `Unchanged` / `Changed` / `Missing` / `Unknown`
- `EvidenceValidationResult`: Evidence単位の状態、パス、理由、dirty状態
- `ValidateKnowledgeResult`: Toolから返す全体結果
- Git diffを表す、MCPやGit CLIに依存しない構造化型

シンボル範囲の再探索は、Git、SQLite、MCP SDKへ依存しない純粋なCoreロジックへ分離する。既存の`ContentHasher`と同じ正規化規則を唯一のハッシュ規則として再利用する。

### 4.2 Infrastructure

`IGitRepository`を、以下の情報を取得できるよう拡張する。

- commit-ishを完全なコミットハッシュへ解決できるか
- 2コミット間の変更パス、rename、0行contextのdiff hunk
- 指定コミットにファイルが存在するか、およびその内容
- 現在のworking treeで変更されたパス

`GitCliRepository`がGit CLIを使って実装する。既存どおりシェルを介さず、`ProcessStartInfo.ArgumentList`へ引数を分けて渡す。Gitコマンド文字列や生のdiffテキストはApplication層へ漏らさず、構造化結果へ変換する。

### 4.3 MCP

`CodeKnowledgeTools`へ`validate_knowledge`を追加し、`Program.cs`で`ValidateKnowledgeUseCase`をDI登録する。MCP Toolハンドラーは入力変換とUseCase呼び出しだけを行い、Gitコマンド、ハッシュ判定、集約ロジックを持たない。

### 4.4 SQLite

Phase 1の`knowledge_versions`と`evidence`には、base commit、file hash、symbol hash、保存時の行範囲が存在する。`IKnowledgeStore.GetDetail`で全項目を取得できるため、スキーママイグレーションは行わない。

検証処理は読み取り専用とし、SQLiteへ結果を保存しない。

## 5. Tool契約

### 5.1 入力

```json
{
  "workingDirectory": "C:\\work\\order-system",
  "knowledgeId": "knowledge-001",
  "targetCommit": "HEAD"
}
```

- `workingDirectory`: 必須。現在のGitリポジトリ配下の絶対パス
- `knowledgeId`: 必須。現在のプロジェクトに属するナレッジID
- `targetCommit`: 任意。省略時は`HEAD`。空白文字列は`invalid_arguments`

Phase 2では最新確定バージョンだけを検証する。`versionId`は入力に追加しない。

### 5.2 出力

```json
{
  "status": "partially_stale",
  "baseCommit": "abc123...",
  "targetCommit": "def456...",
  "isWorkingTreeDirty": false,
  "changedEvidence": ["SmtpEmailSender.SendAsync"],
  "unchangedEvidence": ["OrderService.CompleteAsync"],
  "missingEvidence": [],
  "unknownEvidence": [],
  "dirtyEvidence": [],
  "evidenceDetails": [
    {
      "evidenceId": "evidence-001",
      "label": "SmtpEmailSender.SendAsync",
      "originalFilePath": "src/Mail/SmtpEmailSender.cs",
      "targetFilePath": "src/Mail/SmtpEmailSender.cs",
      "status": "changed",
      "reason": "symbol_hash_not_found",
      "isWorkingTreeDirty": false
    }
  ],
  "recommendedAction": "reinspect_changed_symbols",
  "warnings": []
}
```

Evidenceの表示ラベルは、要件6.3の識別優先順位に合わせ、`symbol_id`、`signature`、`symbol_name`、`file_path`の順で最初に利用できる値を使う。

`changedEvidence`、`unchangedEvidence`、`missingEvidence`、`unknownEvidence`、`dirtyEvidence`はラベルの配列とする。詳しい位置と判定理由は`evidenceDetails`で返す。

`isWorkingTreeDirty`は、確認成功時はboolean、確認処理自体が失敗した場合は`null`とする。確認不能を`false`として扱わない。

`evidenceDetails.reason`はAgentとテストが分岐に使える安定したコード値とし、少なくとも以下を定義する。

- `file_hash_match`
- `symbol_hash_match_at_mapped_range`
- `symbol_hash_match_at_moved_range`
- `target_file_missing`
- `symbol_hash_not_found`
- `symbol_hash_unavailable`
- `base_file_unavailable`
- `target_file_unavailable`
- `commit_unavailable`
- `diff_unavailable`
- `dirty_check_unavailable`

## 6. 検証フロー

### 6.1 準備

1. 現在のプロジェクトを解決する。
2. 現在の`project_id`を条件に最新確定ナレッジを取得する。
3. ナレッジがなければ`knowledge_not_found`を返す。
4. 保存済みバージョンの`commit_hash`をbaseとする。
5. `targetCommit`を完全なcommit hashへ解決する。省略時はプロジェクト解決時のHEADを使う。
6. baseとtargetの比較情報を取得する。
7. Evidenceの元パスを、rename情報を使ってtarget側パスへ対応付ける。

`targetCommit`が`HEAD`以外の場合でも、dirty判定は常に現在のworking treeに対して行う。Evidenceのbaseパスを現在HEADまでのrename情報で対応付け、現在のGit statusに含まれるパスと照合する。targetが現在HEADなら、鮮度検証用のrename情報を再利用する。

### 6.2 Evidence単位の判定

次の順序で判定する。

1. rename対応後のtargetパスにファイルが存在しなければ`missing`。
2. targetファイルのfile hashが保存済み`file_hash`と一致すれば`unchanged`。
3. ファイルが変わっており、`symbol_hash`、`start_line`、`end_line`のいずれかがなければ`unknown`。
4. baseファイルを読み、保存時範囲の実効行数を求める。保存時に`end_line`がファイル末尾を越えていた場合は、既存`ContentHasher`と同じように末尾へクランプする。
5. diff hunkからtarget側の候補開始行を求め、その実効行数の範囲を既存規則でハッシュする。
6. 候補が一致しなければ、targetファイル全体を同じ実効行数のwindowで走査する。
7. 保存済み`symbol_hash`と一致するwindowが1つ以上あれば`unchanged`。
8. ファイルは存在するが一致するwindowがなければ`changed`。
9. base内容、行範囲、diff、target内容を確定できなければ`unknown`。

一致判定はハッシュだけで行う。`symbol_id`、`signature`、`symbol_name`はPhase 2では表示とAgentの再調査支援に使い、言語解析による位置特定には使わない。

コメント変更はシンボル範囲ハッシュを変えるため`changed`になる。空白は既存の`ContentHasher.ComputeSymbolHash`が行う、改行統一、行末空白除去、連続する空白・タブの縮約に従う。

### 6.3 全体ステータス

Evidenceごとの結果を以下の優先順位で集約する。

| 条件 | 全体ステータス |
|---|---|
| Evidenceが0件 | `unknown` |
| 1件以上が`unknown` | `unknown` |
| 全件が`unchanged` | `valid` |
| 全件が`changed`または`missing` | `stale` |
| 上記以外の混在 | `partially_stale` |

これにより、1件でも再利用可能な根拠が残る場合、変更・削除されたEvidenceがあっても`partially_stale`とする。Evidenceには主要・補助の重要度がないため、1件の削除だけでナレッジ全体を`stale`にはしない。

例:

| Evidence内訳 | 判定 |
|---|---|
| unchanged × 3 | `valid` |
| unchanged × 1、changed × 1、missing × 1 | `partially_stale` |
| changed × 2、missing × 1 | `stale` |
| unchanged × 1、unknown × 1 | `unknown` |

### 6.4 `symbol_hash`がない既存Evidence

Phase 1では行範囲が任意入力であり、`symbol_hash`がないEvidenceを保存できる。その場合は次の安全なフォールバックを使う。

- targetのfile hashが一致する: `unchanged`
- targetファイルは存在するがfile hashが変わった: `unknown`
- targetファイルが存在しない: `missing`

ファイルが変わっただけで、根拠シンボルまで変わったと推測して`changed`にはしない。

### 6.5 renameと移動

Gitのrename検出でbaseパスとtargetパスを対応付ける。内容が同一のファイルrenameはfile hash一致で`unchanged`になる。ファイルの他部分も変更された場合は、targetパス内でシンボル範囲ハッシュを再探索する。

Gitがrenameとして検出できず、元パスがtargetに存在しない場合は`missing`とする。Phase 2ではリポジトリ全体から似たファイルを探索しない。

### 6.6 working tree dirty判定

`isWorkingTreeDirty`は、保存済みEvidenceに対応する現在HEAD上のファイルに、次のいずれかがある場合に`true`とする。

- staged変更
- unstaged変更
- 削除
- renameまたは移動

dirty判定はコミット間の鮮度ステータスを変更しない。`valid`かつdirtyの場合は、`recommendedAction`を`inspect_dirty_evidence`とし、Agentへ該当ファイルの直接確認を促す。

Git status取得または現在パスへの対応付け自体に失敗した場合は、`isWorkingTreeDirty = null`、全体ステータス`unknown`、warning付きで返す。

## 7. 推奨アクション

`recommendedAction`は次の値に限定する。

| 条件 | 値 | 意味 |
|---|---|---|
| `valid`かつdirtyでない | `reuse_knowledge` | 保存済みナレッジを主に利用する |
| `valid`かつdirty | `inspect_dirty_evidence` | dirtyなEvidenceを直接確認する |
| `partially_stale` | `reinspect_changed_symbols` | changed、missing、およびdirtyなEvidenceを確認する |
| `stale` | `reinvestigate_knowledge` | ナレッジ全体を再調査する |
| `unknown` | `inspect_evidence` | unknownを含む根拠コードを直接確認する |

dirty状態は`dirtyEvidence`にも常に明示し、`partially_stale`、`stale`、`unknown`でもAgentが未コミット変更を見落とさないようにする。

## 8. エラー処理

### 8.1 Toolエラー

次は既存の`CodeKnowledgeException`と`ToolGuard`を通じてToolエラーにする。

- Gitリポジトリ外: `git_repository_required`
- Git CLI不在: `git_not_found`
- 空の`workingDirectory`、`knowledgeId`、明示的な空`targetCommit`: `invalid_arguments`
- 現在のプロジェクトにナレッジがない: `knowledge_not_found`
- DB busy: `database_busy`
- 予期しないDB障害、Gitプロセス障害、実装不整合: `internal_error`

### 8.2 正常な`unknown`結果

検証対象の不確実性はToolエラーにせず、正常な構造化結果として返す。

- baseまたはtargetコミットがローカルに存在しない
- shallow clone等で必要なGitオブジェクトが不足している
- `symbol_hash`がなく、ファイルが変更されている
- Evidenceの位置または内容を確定できない
- Evidenceが0件
- working tree dirty判定を確定できない

存在しないtarget commit-ishの場合、`targetCommit`は解決できないためnullとし、入力値と理由をwarningへ含める。秘密情報やコード本文はwarningやログへ含めない。

baseまたはtargetを解決できずEvidence検証を開始できない場合は、全Evidenceを`unknownEvidence`へ入れ、各`evidenceDetails.reason`を`commit_unavailable`とする。diffだけを取得できない場合も同様に`diff_unavailable`とする。これにより、早期終了時もEvidence内訳を欠落させない。

## 9. 性能とキャッシュ

1回のUseCase実行中だけ、以下をキャッシュする。

- commitごとのファイル内容とfile hash
- ファイルごとの正規化済み行
- 同じファイル・同じwindow長に対する探索結果
- baseからtargetへのdiffとrename情報

targetが現在HEADなら、dirty判定用のbaseからHEADへのrename情報も再利用する。targetが現在HEAD以外なら、dirty判定のためbaseから現在HEADへの対応付けを追加で1回取得してよい。

SQLiteへの書き込みとプロセスを跨ぐキャッシュは行わない。Evidence対象ファイルだけを読み、リポジトリ全体のコード内容を走査しない。

## 10. テスト戦略

TDDでCore、Infrastructure、MCPの順に実装する。

### 10.1 Core単体テスト

- 全Evidence unchangedで`valid`
- unchangedとchanged/missingの混在で`partially_stale`
- 全Evidence changed/missingで`stale`
- unknownが1件でもあれば`unknown`
- Evidence 0件で`unknown`
- file hash一致のfast path
- `symbol_hash`なしでfile hash一致なら`unchanged`
- `symbol_hash`なしでファイル変更なら`unknown`
- diff候補範囲でのシンボルハッシュ一致
- 同一ファイル内の別位置をwindow探索して一致
- 同じハッシュが複数箇所にあっても`unchanged`
- 一致windowなしで`changed`
- 保存時`end_line`がファイル末尾を越える場合の実効行数
- コメント変更は`changed`、許容する空白差分は`unchanged`
- dirtyは鮮度ステータスを変えず、推奨アクションへ反映
- target commit解決不能、base欠落、dirty判定不能の`unknown`
- ナレッジを現在のproject_idで取得すること

### 10.2 Infrastructure統合テスト

一時Gitリポジトリを使って次を検証する。

- `HEAD`、短縮hash、完全hash、タグのcommit解決
- 存在しないcommit-ish
- ファイル変更、追加、削除のdiff
- 内容を維持したrenameと移動
- rename後に内容も変更したケース
- 行追加・削除を含む0行context hunkの解析
- staged、unstaged、削除、renameのworking tree status
- 空白、日本語を含むパス
- worktreeとdetached HEAD
- 引数配列経由であり、commit-ishやパスをシェルとして解釈しないこと

### 10.3 MCP E2E

発行済み実行ファイルをJSON-RPCクライアントから起動して次を検証する。

- `validate_knowledge`がTool一覧へ公開される
- 保存直後のHEAD検証が`valid`
- 一部根拠変更が`partially_stale`
- 全根拠変更が`stale`
- 指定コミットをtargetにできる
- Evidenceファイルの未コミット変更で`isWorkingTreeDirty = true`（AC-25）
- Gitリポジトリ外ではSQLiteへ書き込まず`git_repository_required`
- stdoutへログが混入しない

Windows 11 x64とmacOS Apple Siliconの既存ビルド・発行経路を維持する。

### 10.4 受け入れ条件対応

- AC-04: baseと現在HEADを比較し、変更Evidenceと未変更Evidenceを区別する
- AC-05: 指定コミットをtargetとして検証する
- AC-11: MCPを介さずCore UseCaseを単体テストする
- AC-12: 検証ロジックを将来CLIから再利用できる
- AC-13: Gitリポジトリ外で処理しない
- AC-14: 必要なcommitを取得できないとき比較を実行しない
- AC-25: Evidence対象の未コミット変更を通知する

Phase 1の既存受け入れ条件は全テストで回帰確認する。

## 11. ドキュメント更新

`README.md`を次の内容で更新する。

1. Tool一覧へ`validate_knowledge`を追加する。
2. 入力例と出力例を追加する。
3. 4つの全体ステータスとEvidence単位ステータスを説明する。
4. `recommendedAction`の意味を説明する。
5. `isWorkingTreeDirty`がtrueまたはnullなら該当ファイルを直接確認するよう明記する。
6. Agent向け行動ルールを、検索後に`validate_knowledge`を呼ぶPhase 2の手順へ更新する。
7. シンボル名変更の意味的追跡やRoslyn解析がPhase 2対象外であることを明記する。

MCPクライアントの登録形式とDB配置は変わらない。

## 12. 要件の具体化と追加出力

要件9.2の「主要根拠」にはデータモデル上の重要度がないため、本設計では次のように具体化する。

- 一部でも`unchanged`が残る: `partially_stale`
- 全件が`changed`または`missing`: `stale`
- 判定不能が混ざる: `unknown`

要件10.4の出力例へ、Agentが直接確認する場所と理由を特定するため、以下を追加する。

- `unknownEvidence`
- `dirtyEvidence`
- `evidenceDetails`
- `warnings`

また、dirty確認不能を偽の`false`にしないため、要件例ではbooleanの`isWorkingTreeDirty`をnullable booleanとして具体化する。確認成功時の`true` / `false`契約とフィールド名は維持し、確認失敗時だけ`null`を許容する。その他の項目は既存フィールドを削除せずに行う出力拡張である。

## 13. 完了条件

- `validate_knowledge`がCore UseCase、Infrastructure Gitアダプター、MCP薄層の責務分離で実装されている。
- 合意したEvidence判定と全体集約規則が自動テストで固定されている。
- ファイルrename、同一ファイル内の行移動、`symbol_hash`欠落を安全に扱える。
- Evidence対象の未コミット変更を検出できる。
- 検証処理がDBへ書き込まない。
- Phase 1の全自動テストが回帰成功する。
- WindowsとmacOSの発行構成を壊さない。
- READMEとAgent向けルールがPhase 2の利用フローを説明している。
- Phase 3以降の機能を実装していない。
