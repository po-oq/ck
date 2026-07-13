# CodeKnowledge Windows / macOS対応・リリース自動化設計

**日付:** 2026-07-13

**状態:** 承認済み

**対象:** Phase 1完了後のクロスプラットフォーム対応

## 1. 目的

CodeKnowledgeの正式対応環境を、既存のWindows 11 x64にApple Silicon Macを加えた2環境へ拡張する。両OSで同じ機能、データ形式、MCP契約を提供し、GitHub Actionsで継続的に検証する。`v*`タグをpushした際は、Windows版とMac版を同じGitHub Releaseへ自動的に添付する。

この設計は、既存のPhase 1設計にあるWindows専用の配布・E2Eテスト方針を、クロスプラットフォーム対応に関する範囲で上書きする。MCP Tool、SQLiteスキーマ、検索・保存ロジックなど、ここで明示しないPhase 1契約は変更しない。

## 2. 対応範囲

正式対応するOS、CPU、Runtime Identifier（RID）は次のとおりとする。

| OS | CPU | RID | 実行ファイル |
|---|---|---|---|
| Windows 11 | x64 | `win-x64` | `CodeKnowledge.Mcp.exe` |
| macOS 15以降 | Apple Silicon arm64 | `osx-arm64` | `CodeKnowledge.Mcp` |

Linux、Intel Mac、Windows Arm64は対象外とする。テストfixtureを未対応環境で実行した場合は、RIDを推測したりテストをskipしたりせず、未対応プラットフォームであることを明示して失敗させる。

両OSとも.NET 10のframework-dependent単一ファイルとして発行する。利用者側には対応する.NET 10 Runtimeが必要となる。Mac版は今回、Developer ID署名およびApple公証を行わない。

## 3. アーキテクチャ

アプリケーション機能、MCP Tool、SQLiteスキーマ、ワイヤ形式は両OSで共通とする。OS差分は以下の境界に限定する。

- 発行時のRID
- 実行ファイルの拡張子
- ローカルファイルパスの解析と正規化
- Releaseアーカイブ形式
- 利用者向けのインストールおよびMCPクライアント設定

MCP E2Eテストは、実行中のOSとCPUに対応するRIDで発行した実バイナリを起動する。WindowsとMacで同じ8件のE2Eテストを実行し、OSごとにテスト内容を弱めない。

## 4. パス処理とproject ID

### 4.1 パス形式の判定

Windows形式のパスをmacOS上のテストでも正しく扱えるよう、入力パスの形式を考慮した字句的な正規化を行う。ホストOSの`Path` APIだけにWindows形式の解釈を委ねない。

- ドライブ文字またはバックスラッシュを含むWindows形式は、`\`と`/`を区切りとして扱う。
- Windows形式では大文字小文字を同一視する。
- Unix形式では`/`を区切りとして扱い、大文字小文字を保持する。
- 末尾の`/`または`\`はlocal project IDへ影響させない。
- display nameは、どちらの区切り形式でも最後のディレクトリ名から取得する。

### 4.2 local project ID

remoteがないGitリポジトリのlocal project IDは、従来どおり正規化済み絶対パスから生成する。WindowsとMacでは絶対パスが異なるため、OS間で同じlocal project IDを生成することは要件としない。同一OS・同一パスでは、末尾区切りやWindowsパスの大文字小文字の差によらず安定することを保証する。

remote URL由来のproject IDはOSに依存せず、WindowsとMacで同じ値となる。

### 4.3 Git repository root

Gitの`rev-parse --show-toplevel`が返す物理パスを正規のrepository rootとして扱う。macOSでは`/var`と`/private/var`のように同じ場所を指す複数表現が存在するため、統合テストは未正規化の文字列一致ではなく、正規化後に同じファイルシステム位置を指すことを検証する。

製品コードにはテスト専用分岐を追加しない。

## 5. データ互換性

クロスプラットフォーム対応によってSQLiteスキーマを変更しない。

- evidenceの`filePath`はリポジトリ相対かつ`/`区切りを維持する。
- remote由来project IDは両OSで同一とする。
- Windowsで作成したDBをMacへ、Macで作成したDBをWindowsへ移して読み取れる形式を維持する。
- 既定DBパスは実行ファイル隣接の`knowledge.db`を維持する。
- `CODEKNOWLEDGE_DB_PATH`による上書きを両OSで維持する。

## 6. テスト戦略

### 6.1 Core.Tests

Windows形式とUnix形式のパス正規化を純粋ロジックとして検証する。少なくとも以下を固定する。

- Windowsパスの大文字小文字の差がlocal project IDへ影響しない。
- Windowsパスの`\`と`/`、末尾区切りの差がlocal project IDへ影響しない。
- Unixパスの末尾区切りがlocal project IDへ影響しない。
- Windows形式のパスから、ホストOSにかかわらず正しいdisplay nameを取得する。
- config、remote、localの既存優先順位を維持する。

### 6.2 Infrastructure.Tests

各OS上で実Gitリポジトリとworktreeを作成し、Gitが返したrepository root、HEAD、branchを検証する。macOSのシンボリックリンクを含むパス表現は、同じ物理位置を指すよう正規化して比較する。Windows向けの期待値へMac専用文字列を追加するだけの対処は行わない。

### 6.3 Mcp.Tests

`PublishedServerFixture`は実行中のOSとCPUを検査し、次の組み合わせだけを許可する。

- Windows x64: `win-x64`、`CodeKnowledge.Mcp.exe`
- macOS arm64: `osx-arm64`、`CodeKnowledge.Mcp`

fixtureはテストアセンブリごとに一度、対応RIDのframework-dependent単一ファイルを一時ディレクトリへ発行する。発行済み実バイナリをstdio transportで起動し、既存の8件を両OSで同一に実行する。skipは追加しない。

## 7. 継続的インテグレーション

`.github/workflows/ci.yml`を追加し、次のイベントで実行する。

- `main`へのpush
- `main`向けPull Request
- `workflow_dispatch`

runner matrixは次の固定ラベルを使用する。

| Runner | Architecture | E2E / publish RID |
|---|---|---|
| `windows-2025` | x64 | `win-x64` |
| `macos-15` | arm64 | `osx-arm64` |

各jobはソースをcheckoutし、.NET SDK 10.0.201をセットアップした後、次を実行する。

1. restore
2. Release構成の全テスト
3. 対象RIDを明示したRelease publish
4. SQLiteのネイティブ依存物を含む必要ファイルの存在確認
5. 発行バイナリとのstdio接続およびMCP初期化確認

ビルド成功だけではCI成功とせず、発行済み実バイナリとのstdio接続、MCP初期化、MCP E2E成功を必須とする。標準入力を待つMCPサーバーを単に起動して一定時間待つ方式は、起動確認として採用しない。

## 8. GitHub Release

`.github/workflows/release.yml`を追加し、`v*`タグのpushで起動する。タグは`v1.0.0`または`v1.1.0-beta.1`のようなSemVer形式であることを検証し、不正なタグではReleaseを作成しない。

WindowsとMacのbuild jobは、それぞれ次を実行する。

1. .NET SDK 10.0.201のセットアップ
2. Release構成の全テスト
3. 対象RIDでのframework-dependent単一ファイルpublish
4. publishディレクトリ全体のパッケージ化
5. workflow artifactへのアップロード

SQLiteのネイティブライブラリなどを確実に含めるため、実行ファイル単体ではなくpublishディレクトリ全体を配布する。WindowsはZIP、Macは実行権限を保持するtar.gzとする。

両OSのjobがすべて成功した後だけRelease作成jobを実行し、次の3ファイルを同じGitHub Releaseへ添付する。

```text
CodeKnowledge-v1.0.0-win-x64.zip
CodeKnowledge-v1.0.0-osx-arm64.tar.gz
SHA256SUMS
```

ファイル名のバージョン部分にはタグ名を使用する。Release notesはGitHubの自動生成を利用する。途中で一方のbuild、test、packageが失敗した場合はReleaseを作成しない。既存Releaseの成果物を暗黙に上書きしない。

## 9. エラー処理

- 未対応OSまたはCPUでE2E fixtureを実行した場合、検出したOS・CPUと対応範囲を含む明確なエラーを返す。
- `dotnet publish`、テスト、必要ファイル確認、アーカイブ作成のいずれかが失敗した場合、そのjobを失敗させる。
- 両OSの成果物が揃わない状態でGitHub Releaseを作成しない。
- Mac版は未署名であることをReleaseおよびREADMEで明示する。
- Gatekeeperにより起動を拒否された場合の対処は、信頼した公式Release成果物に限定して案内する。

## 10. ドキュメント

READMEをWindows／Mac両対応へ更新し、次を記載する。

- 対応OSの最低バージョン、CPU、RID
- .NET 10 Runtimeの前提
- 各Releaseアーカイブの選び方と展開方法
- WindowsとMacそれぞれの起動方法
- Claude CodeなどのMCPクライアント設定例
- 各OSの既定DB配置と`CODEKNOWLEDGE_DB_PATH`
- Mac版が未署名・未公証であること
- 必要な場合の`chmod +x`とquarantine解除手順
- Linux、Intel Mac、Windows Arm64が対象外であること

## 11. 完了条件

以下をすべて満たした時点で完了とする。

1. ローカルApple Silicon Macで全128テストが成功する。
2. GitHub ActionsのWindows x64とmacOS arm64で全テストが成功する。
3. 両runnerで対象RIDのpublishと発行バイナリの起動確認に成功する。
4. OS対応のためのテストskipを追加していない。
5. `v*`タグから両アーカイブと`SHA256SUMS`を含むGitHub Releaseを作成できる。
6. Macのtar.gzを展開した後も実行権限が保持される。
7. WindowsとMacでSQLiteスキーマおよび保存形式に差がない。
8. READMEだけで両OSの導入とMCPクライアント設定まで到達できる。

## 12. 対象外

- Linux対応
- Intel Mac対応
- Windows Arm64対応
- self-contained配布
- macOS Developer ID署名
- Apple公証
- インストーラパッケージの作成
- OS間でのlocal project ID統一
