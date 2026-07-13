# Phaseロードマップ利用価値追記 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 要件定義のPhase 2〜4へ、各Phaseで利用者が得る価値と具体的な利用イメージを追加する。

**Architecture:** 14章の既存Phase見出しと機能一覧の間に、利用者目線の説明を追記する。機能範囲・Phase順序・受け入れ条件は変更せず、Phase 4が必要時のみ実施される位置づけも維持する。

**Tech Stack:** Markdown

## Global Constraints

- Phase 0・1には利用価値説明を追加しない。
- Phase 2〜4の既存機能一覧を削除または変更しない。
- 新しいTool、データモデル、受け入れ条件を追加しない。
- Phase 2とPhase 3の境界を維持する。
- Phase 4を必須Phaseへ変更しない。

---

### Task 1: Phase 2〜4の利用価値を要件定義へ追記する

**Files:**
- Modify: `docs/code-knowledge-tool-requirements-v2.md:1598-1625`

**Interfaces:**
- Consumes: `docs/superpowers/specs/2026-07-13-phase-roadmap-user-value-design.md`
- Produces: Phase 2〜4の目的と利用場面を機能一覧と同じ場所で確認できるロードマップ

- [x] **Step 1: Phase 2へ利用価値と利用イメージを追加する**

`### Phase 2: 鮮度検証`と既存機能一覧の間へ次を追加する。

```markdown
**このPhaseで便利になること:** 保存済みナレッジが現在のコードでも信用できるかを判定できる。コード全体を毎回調べ直す代わりに、変更されていない根拠は再利用し、変更された根拠だけを再調査できる。根拠ファイルに未コミット変更がある場合も検知し、保存済みの判定を過信することを防ぐ。

**利用イメージ:** Agentが過去ナレッジを検索した後に`validate_knowledge`を実行する。`valid`なら必要最小限の確認で回答し、`partially_stale`なら変更箇所だけを調べ直し、`stale`または`unknown`なら実コードを再調査する。
```

- [x] **Step 2: Phase 3へ利用価値と利用イメージを追加する**

`### Phase 3: 差分保存`と既存機能一覧の間へ次を追加する。

```markdown
**このPhaseで便利になること:** 過去の調査時点から何が変わったかを比較し、その変更内容を後の調査で再利用できる履歴として残せる。ナレッジが現在の内容になった経緯を追跡できるほか、不要になった旧バージョンの整理やProject ID変更時のナレッジ移行も行える。

**利用イメージ:** 仕様変更後にAgentが`compare_knowledge`で保存時点と現在を比較して変更内容を説明する。ユーザーが保存に同意した場合だけ、`save_comparison`で差分と更新後のナレッジを永続化する。
```

- [x] **Step 3: Phase 4へ利用価値と利用イメージを追加する**

`必要になった場合のみ実施する。`の後、既存機能一覧の前へ次を追加する。

```markdown
**このPhaseで便利になること:** 保存時とは異なる表記や言い回しの質問でも、意味的に近いナレッジを見つけやすくなる。またC#コードでは、整形やコメント変更など処理の意味に影響しない変更によって、ナレッジを古いと誤判定するケースを減らせる。

**利用イメージ:** 利用者が保存時とは異なる用語で質問しても、同義語展開と意味検索によって関連ナレッジが候補に含まれる。実運用でPhase 1〜3の検索精度または鮮度判定精度が不足した場合にのみ導入する。
```

- [x] **Step 4: 文面とスコープを検証する**

Run:

```bash
sed -n '1598,1645p' docs/code-knowledge-tool-requirements-v2.md
rg -n "このPhaseで便利になること|利用イメージ" docs/code-knowledge-tool-requirements-v2.md
git diff --check -- docs/code-knowledge-tool-requirements-v2.md
```

Expected:

- Phase 2〜4に`このPhaseで便利になること`と`利用イメージ`が各1件、合計6件ある。
- Phase 0・1には追加されていない。
- Phase 2〜4の既存機能一覧が残っている。
- `git diff --check`が終了コード0になる。

- [x] **Step 5: 変更をコミットする**

```bash
git add docs/code-knowledge-tool-requirements-v2.md docs/superpowers/plans/2026-07-13-phase-roadmap-user-value.md
git commit -m "docs: explain user value of phases 2 through 4"
```
