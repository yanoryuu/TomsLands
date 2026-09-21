---
name: utage-resource-management
description: "Use this skill whenever the user wants to package UTAGE resources into AssetBundles for external/server-hosted delivery, or convert a folder of full character-pose images into UTAGE's 'dicing' format to save texture memory — e.g. 'UTAGEのAssetBundleをビルドして', 'リソースをダウンロード配信用にパッケージ化したい', 'このキャラの立ち絵をダイシング形式に変換して', 'ポーズ差分のテクスチャメモリを減らしたい'. Covers the Resource Converter (AssetBundle build) and Dicing Converter windows. This is an advanced/optional distribution and optimization step, not something every project needs. Do NOT use for writing or rebuilding scenario content (see utage-write-scenario), initial project setup (see utage-setup-and-overview), or C# extension (see utage-extend-with-code). When in doubt whether a request is about AssetBundle packaging or dicing conversion specifically (not general resource setup), use this skill."
metadata:
  asset: "UTAGE (Unity Text Adventure Game Engine) Version4"
  publisher: "Ryohei Tokimura (Madnesslabo)"
  asset-version: "4.2.9"
  skill-version: "1.0.0"
  unity: "6000.0.58f2+"
  render-pipelines: "Built-in, URP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/utage4-for-unity-text-adventure-game-engine-version4-266447"
  documentation-url: "https://madnesslabo.net/utage/"
  support-url: "https://madnesslabo.net/utage/"
  last-verified: "2026-09-01"
---

# UTAGEリソースの変換・パッケージ化

UTAGEが用意する、リソース側の作業のための2つの独立した任意ツールを扱う: 外部/サーバー配信用に
**AssetBundle**をビルドするツール（Resource Converter）と、立ち絵の差分画像一式をテクスチャ
メモリ節約のためにUTAGEの**ダイシング**形式へ変換するツール（Dicing Converter）。
どちらも日常的なシナリオ執筆の一部ではない — `Resources`から直接シナリオコンテンツを
再生するだけの大半のプロジェクトでは、このスキルは不要。

## When to use this skill

- UTAGEリソースのAssetBundleをビルドしたいとき（例: シナリオ/メディアアセットをアプリに
  同梱せずサーバーでホストしたい）
- 立ち絵の差分画像一式をUTAGEのダイシング形式に変換したいとき
- Not for: シナリオ内容の記述・リビルド（`utage-write-scenario`参照）、
  初回のプロジェクトセットアップ（`utage-setup-and-overview`参照）、
  C#による拡張（`utage-extend-with-code`参照）

## Prerequisites

- UTAGEプロジェクトが既にセットアップされており、`AssetFileManager`コンポーネントを含む
  シーンが開いていること — Resource Converterはこれを必須とし、無い場合は
  `"FileManager is not found in current scene"`とログを出して処理を中断する
- AssetBundleビルドの場合: 対象プラットフォームを把握しておく。UTAGEのコンバーターは
  デフォルトでEditorのプラットフォーム向けのみビルドする
- ダイシング変換の場合: 変換元の立ち絵差分画像が既にフォルダに揃っていること

**プログラム的な確認方法** — 型`Utage.AdvResourcesConverter`と`Utage.DicingConverter`が
存在すること、AssetBundleビルドを試みる前に現在開いているシーンに`Utage.AssetFileManager`
コンポーネントが存在することを確認する。

## Quick start（Resource ConverterによるAssetBundleビルド）

1. `AssetFileManager`を含むUTAGEシーン（例: `utage-setup-and-overview`で作成したシーン）が
   開いていることを確認する
2. `Tools > Utage > Resource Converter`を開く
3. "Resources Directory"（リソースフォルダ）または"Project Setting"
   （`AdvScenarioDataProject`アセット）のどちらかを変換元として設定する — どちらかと
   出力パスの両方が設定されるまでConvertボタンは無効のまま
4. 出力パス（パスピッカーでディスク上のディレクトリ）を設定する
5. AssetBundleのオプションを確認する（ビルドモード: なし／エディタのみ／全プラットフォーム、
   リネーム方式、対象プラットフォームフラグ、ビルドオプション）— デフォルトは
   エディタのみ・Windows対象
6. **Convert**をクリックする

期待される結果: 選択した出力パスにAssetBundleがビルドされる。エラーは`Debug.LogException`で
ログに出るため、黙って握りつぶされることはない — コンソールを確認すること。

## Workflows

### Workflow: 特定プロジェクトのAssetBundleビルド

**Goal:** UTAGEプロジェクトのリソースを、アプリに直接同梱する代わりにAssetBundleとして
パッケージ化する。

**Steps:** Quick startの手順1〜6を実行する。変換元は生のフォルダではなく、
プロジェクトの`<ProjectName>.project.asset`を指した"Project Setting"を使う。

**Expected result:** 選択した出力パスにそのプロジェクトのリソースのAssetBundleが存在し、
ビルド中にコンソールへ例外がログ出力されていないこと。

### Workflow: 立ち絵差分画像のダイシング形式への変換

**Goal:** ポーズ・表情差分が多いキャラクターの、差分画像一式をUTAGEのダイシング形式に変換して
テクスチャメモリを削減する。

**Steps:** `Tools > Utage > Dicing Converter`を開く。キャラクター/画像セットごとに、
入力フォルダと2つの出力フォルダ（生成されるダイシングデータ用、生成されるテクスチャ用）を
設定する。

**Expected result:** 指定した出力フォルダにダイシングデータとテクスチャアセットが生成され、
全差分画像を非圧縮の個別テクスチャとして保持する必要がなくなる。

## Verification

- AssetBundleビルド後: 出力パスにビルドされたバンドルが存在し、Convert実行時に
  コンソールへ例外がログ出力されていないこと
- ダイシング変換後: 両方の出力フォルダに生成済みアセットが存在し、コンソールにエラーが
  出ていないこと

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools > Utage > Resource Converter` | Editorメニュー項目 | リソースフォルダまたは`AdvScenarioDataProject`からAssetBundleをビルドする。開いているシーンに`AssetFileManager`が必要 |
| `Tools > Utage > Dicing Converter` | Editorメニュー項目 | 立ち絵差分画像一式のフォルダを、UTAGEのダイシングデータ＋テクスチャ形式に変換する |

## Common issues

- **"FileManager is not found in current scene"とログが出て何も起きない** — 原因: 現在開いている
  シーンに`AssetFileManager`コンポーネントが存在しない。対処: それを含むUTAGEシーンを開く
  （例: `utage-setup-and-overview`のNew Projectウィザードが作るシーン）
- **Convertボタンが無効のまま押せない** — 原因: Resources DirectoryもProject Settingも
  設定されていない、または出力パスが空欄。対処: どちらか一方の変換元と、空でない出力パスを設定する
- **Convert実行中に例外がログ出力される** — このツールは黙って失敗するのではなく捕捉して
  ログ出力する（`Debug.LogException`）。当てずっぽうでリトライせず、正確な例外内容を
  ユーザーに報告すること

## Boundaries

- このスキルはAssetBundleパッケージ化とダイシング変換に特化している — 「プロジェクトに
  キャラ画像/サウンドを追加する一般的な方法」ではない（それは`utage-write-scenario`の
  リファレンス07章に書かれている設定シート配下にファイルを置くだけの話）
- シナリオ内容やシナリオデータのリビルドは扱わない — `utage-write-scenario`参照
- 英語話者のユーザーには`Assets/Utage/AI/Skills/`配下の英語版を案内する
