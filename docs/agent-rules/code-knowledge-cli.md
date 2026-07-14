## コード知識ツール（CLI）

既存機能について調べるときは、まず保存済みナレッジを検索してから回答する。

    echo '{"keywords":["認証","トークン"]}' | "C:\Tools\CodeKnowledge\CodeKnowledge.Cli.exe" search

調査が済んだら、ユーザーの明示指示または保存提案への同意後にのみ保存する。
本文が長い・改行を含む場合は、一時ファイルにJSONを書いて --input で渡す（シェルのクォート事故を避ける）。

    "C:\Tools\CodeKnowledge\CodeKnowledge.Cli.exe" save --input C:\Temp\ck-save.json

入力JSONの形が不明なときは `<exe> <command> --help` で確認する。
stdoutはJSON、ログはstderr、終了コードは 0=成功 / 1=入力エラー / 2=前提エラー。
