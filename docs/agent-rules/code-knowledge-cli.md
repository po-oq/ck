# コード知識ツール（Code Knowledge CLI）

このリポジトリには、過去のコード調査結果をプロジェクト単位でSQLiteに蓄積する「コード知識ツール」がCLIとして用意されている。同じ箇所を毎回ゼロから調べ直さないために、**既存機能について尋ねられたら、まずこのツールで過去の調査結果を検索してから回答すること**。

以降、実行ファイルの絶対パスを `CK` と表記する。実際の環境の配置に合わせて置き換えること。
- Windows: `C:\Tools\CodeKnowledge\CodeKnowledge.Cli.exe`
- macOS (Apple Silicon): `$HOME/Tools/CodeKnowledge/CodeKnowledge.Cli`（拡張子なし）

以降のコマンド例はWindows（PowerShell）とmacOS（bash/zsh）のどちらでも、`CK` を各自のパスに置き換えれば動く。標準入力へのJSON渡しはシェルによって書き方が変わるため（PowerShellの `echo '...' |` とbashの `echo '...' |` はクォート解釈が異なる）、**多行や日本語を含む入力は後述の `--input <file>` を使うのが安全**。

## いつ・どのコマンドを使うか（判断表）

| こういうとき | 使うコマンド |
|---|---|
| ユーザーが既存機能の仕様・処理・設計理由を尋ねてきた | まず `search` → ヒットしたら `get` で詳細取得 |
| `search` がヒットしない、または調査が必要 | 自分でコードを調べる → 調査後に保存を提案し、同意を得たら `save` |
| 過去に保存したナレッジが「今も正しいか」怪しい／久しぶりに参照する | `get` の前後で `validate`（根拠コードが変わっていないか確認） |
| このリポジトリがナレッジ上どのプロジェクトとして扱われるか確認したい | `resolve`（通常は各コマンドが内部で解決するので明示実行は稀） |

**基本ワークフロー: 「答える前に search、信用する前に validate、確定を保存する前に同意」。**

## 共通の入出力ルール

- 入力は**JSONオブジェクトを標準入力に渡す**か、**一時ファイルに書いて `--input <file.json>` で渡す**。
- **改行・長文を含む `save` は必ず `--input` を使う**（`echo` によるインラインJSONはWindowsのクォート処理で壊れやすい）。改行はJSON文字列中で `\n` にエスケープすれば保持される。
- 出力は**stdoutにJSONのみ**。ログ・進捗は**stderr**。
- 終了コード: `0`=成功 / `1`=入力エラー（JSON不正・必須欠落・根拠なし保存など）/ `2`=前提エラー（Gitリポジトリ未解決・DB接続不可など）。**コマンド実行後は必ず終了コードとstdoutのJSONを確認すること。**
- 入力スキーマが不確かなときは `CK <command> --help` を実行して形と例を確認する。

---

## search — 保存済みナレッジを検索する

**使う場面:** ユーザーが既存機能について尋ねてきたら最初に必ず実行する。

**入力:** `{ "keywords": ["..."], "limit": 10 }`（`limit` は省略可、既定10・最大50）

**キーワードは積極的に展開する**（ヒット率が上がる）:
- 質問文中の名詞・複合語（「注文完了メール」→「注文」「メール」「注文完了」）
- 英語訳（メール→mail、仕様→spec）
- 推測されるシンボル名（`OrderCompleted`、`EmailSender`、`MailService`）
- 3文字以上のキーワードを優先。1〜2文字語も部分一致で拾われる。

```
echo '{"keywords":["注文","メール","OrderCompleted","MailSender"]}' | CK search
```

**返り値:** `results[]`（`knowledgeId`, `title`, `confidence`, `canonicalKey` など）。ヒットしたら `knowledgeId` を使って `get` する。ヒットしなければ自分で調査する。

---

## get — ナレッジの詳細を取得する

**使う場面:** `search` でヒットした項目の中身（要約・事実・推論・根拠・関連）を読んでユーザーに説明するとき。

**入力:** `{ "knowledgeId": "<search結果のid>", "versionId": "<省略可: 特定版>" }`

```
echo '{"knowledgeId":"<id>"}' | CK get
```

**返り値:** `summary`（要約）, `facts`（根拠付きの確定事実）, `inferences`（推論とその確信度）, `evidence`（根拠コードの場所）, `relations`。**`facts` は根拠コードに裏付けられた確定事項、`inferences` は推測**。この区別を保ったままユーザーに説明すること。

古い可能性がある場合は、説明の前に `validate` で鮮度を確認する。

---

## validate — ナレッジが今も正しいか検証する

**使う場面:** 保存済みナレッジを参照して回答する前、特に前回調査から時間が経っていそうなとき。根拠コードが現在のHEAD（または指定コミット）から見て変わっていないかを判定する。

**入力:** `{ "knowledgeId": "<id>", "targetCommit": "<省略可: 比較先コミット。省略でHEAD>" }`

```
echo '{"knowledgeId":"<id>"}' | CK validate
```

**返り値と対応:**
- `status: "valid"` → 根拠は変わっていない。そのまま信用してよい。
- `status: "partially_stale"` → 一部の根拠が変化。変わった箇所（`changedEvidence`）は自分でコードを読み直す。
- `status: "stale"` → 大きく陳腐化。**そのまま答えず**、再調査して `save` で更新することを提案する。
- `isWorkingTreeDirty: true` → 未コミット変更がある。該当ファイルは実物を確認する。

`unknown` が返った箇所は必ず直接コードを確認すること。

---

## save — 調査結果をナレッジとして保存する

**使う場面:** コードを調査して仕様・処理フロー・設計事実が固まったとき。**ただし保存は、ユーザーが明示的に「保存して」と指示したか、あなたの保存提案に同意した後にのみ行う。** 勝手に保存しない。長文・多行になりやすいので**`--input` を使う**。

**入力スキーマ（camelCase）:**

| フィールド | 必須 | 説明 |
|---|---|---|
| `canonicalKey` | ✓ | 話題の安定キー。例 `domain.mail.order-completed`。既存の似たキーがあれば揃える |
| `title` | ✓ | 人間可読なタイトル |
| `originalQuestion` | ✓ | 調査のきっかけになったユーザーの質問 |
| `summary` | ✓ | 調査結果の要約（多行可） |
| `confidence` | ✓ | 全体の確信度 `high` / `medium` / `low`（下記基準） |
| `evidence[]` | ✓ | 根拠コードの場所。`{ filePath, symbolName, symbolKind?, signature?, startLine?, endLine?, reason? }` |
| `facts[]` | ✓ | コードから直接確認できた事実。`{ text, evidenceIndexes: [evidenceの添字] }`。**各factは必ず1つ以上のevidenceを参照すること（根拠なしのfactは保存エラー）** |
| `inferences[]` | ✓ | 推測。`{ text, confidence, reason, evidenceIndexes: [...] }`。確証がない内容はfactではなくここに入れる |
| `relations[]` | ✓ | シンボル間の関係 `{ fromSymbol, toSymbol, kind }` |
| `tags` | | スペース区切りタグ |
| `createdBy` | | 記録者（Agent名など） |
| `commitHash` | | 調査時点のコミット。省略でHEAD |

配列に該当がなければ空配列 `[]` を渡す。

**confidence の基準:**
- `high` — 根拠コードを直接読み、実装・呼び出し側・テストで整合を確認した
- `medium` — 主要な根拠は読んだが周辺は未確認
- `low` — 命名や慣習からの推測が中心

**手順:** 一時ファイル（Windowsなら例 `C:\Temp\ck-save.json`、macOSなら例 `/tmp/ck-save.json`）にJSONを書いてから渡す。

```
CK save --input <一時ファイルのパス>
```

最小例（一時ファイルの中身）:

```json
{
  "canonicalKey": "domain.mail.order-completed",
  "title": "注文完了メールの送信処理",
  "originalQuestion": "注文完了メールはどこで送っている？",
  "summary": "OrderService.Complete が MailSender.Send を呼び\n注文完了メールを送信する。",
  "confidence": "high",
  "evidence": [
    { "filePath": "src/OrderService.cs", "symbolName": "OrderService.Complete", "symbolKind": "method", "startLine": 10, "endLine": 24, "reason": "メール送信の起点" }
  ],
  "facts": [
    { "text": "Complete がメール送信を行う", "evidenceIndexes": [0] }
  ],
  "inferences": [],
  "relations": []
}
```

**返り値:** `knowledgeId`, `versionId`, `createdNewKnowledge`（新規か更新か）, `similarKnowledge`（似た既存キーの候補）。`similarKnowledge` が返ったら、キーの乱立を避けるため既存を更新すべきでなかったか確認する。

---

## resolve — 現在のリポジトリをプロジェクトに解決する

**使う場面:** 通常は各コマンドが内部で自動解決するため明示実行は稀。プロジェクトの認識（projectId・ブランチ・コミット）を確認したいときのみ。

**入力:** `{}`（作業ディレクトリはカレント。`--cwd <dir>` で上書き可）

```
echo '{}' | CK resolve
```

**返り値:** `projectId`, `branchName`, `currentCommit` など。Gitリポジトリ外で実行すると終了コード2で失敗する。
