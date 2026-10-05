---
name: utage-extend-with-code
description: "Use this skill whenever the user wants to run custom C# code at a specific point in UTAGE's lifecycle — e.g. 'エンジン初期化時にコードを実行したい', 'パラメーターが変わったら何かしたい', 'ページの開始/終了にフックしたい', 'セーブイベントを購読したい', 'シナリオから自分のコードを呼びたい'. Covers UTAGE's lifecycle-event pattern: AdvEngine and its many child components (AdvPage, AdvSaveManager, AdvParameterEventTrigger, etc.) each expose events you can hook, either by wiring a plain method into a UnityEvent in the Inspector or by subscribing in code. Also briefly covers calling custom C# code from a scenario command, which is documented in depth in the bundled Scenario Reference. Do NOT use for writing or rebuilding scenario content itself (see utage-write-scenario), initial project setup (see utage-setup-and-overview), or resource conversion (see utage-resource-management). When in doubt whether a request needs new C# code rather than existing scenario commands, use this skill."
metadata:
  asset: "UTAGE (Unity Text Adventure Game Engine) Version4"
  publisher: "Ryohei Tokimura (Madnesslabo)"
  asset-version: "4.2.9"
  skill-version: "1.0.1"
  unity: "6000.0.58f2+"
  render-pipelines: "Built-in, URP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/utage4-for-unity-text-adventure-game-engine-version4-266447"
  documentation-url: "https://madnesslabo.net/utage/"
  support-url: "https://madnesslabo.net/utage/"
  last-verified: "2026-09-01"
---

# UTAGEをC#コードで拡張する

UTAGEの主要なコードレベル拡張機構である**ライフサイクルイベントのフック**を扱う。`AdvEngine`と
その配下の多数のコンポーネント（UTAGE自身のコンポーネントリファレンスページに記載）は、
それぞれイベントを公開しており、シナリオ内容に触れずに特定のタイミングでカスタムC#コードを
実行できる。UTAGEのコンポーネント全体にまたがるイベントは数が多く、このスキル内で網羅する
のは現実的ではない — 推測せず調べるのが確実な進め方。シナリオ側からカスタムC#コードを
**呼び出す**方向については、既に同梱のシナリオリファレンスで詳しく扱われているため、
ここでは最低限の言及にとどめる。

## When to use this skill

- エンジン/コンポーネントのライフサイクルの特定タイミング（初期化、ページ開始/終了、
  パラメーター変更、セーブ/ロード等）でコードを実行したいとき
- シナリオコマンドから自分のC#コードを呼び出したいとき
- Not for: 既存コマンドを使ったシナリオ内容の記述・リビルド（`utage-write-scenario`参照）、
  初回のプロジェクトセットアップ（`utage-setup-and-overview`参照）、
  リソース変換（`utage-resource-management`参照）

## Prerequisites

- シーンに`AdvEngine`が存在するUTAGEプロジェクト（`utage-setup-and-overview`参照）
- C#の`MonoBehaviour`スクリプトを書き、GameObjectにアタッチし、Inspectorで`UnityEvent`を
  配線することに慣れていること

**プログラム的な確認方法** — 型`Utage.AdvEngine`がプロジェクト内に存在することを確認する。

## Quick start（ライフサイクルイベントのフック）

1. 必要なイベントを持つコンポーネントを特定する。まずUTAGE自身のコンポーネントリファレンス
   索引（https://madnesslabo.net/utage/?page_id=247 の「Components」節）を確認する。
   そこから各コンポーネントのページ（例: 「AdvEngine Components」
   https://madnesslabo.net/utage/?page_id=446 ）にリンクしており、そのページだけでも
   `AdvEngine`配下の約15個のコンポーネント（`AdvPage`・`AdvSaveManager`・
   `AdvParameterEventTrigger`・`AdvScenarioPlayer`等）が一覧され、それぞれに独自のイベントが
   ある。イベントの存在を推測せず、ここかソースコードで確認すること
2. `Assets/Utage/Sample/Scripts/`内に、必要なイベントを既に使っているスクリプトが無いかも
   確認する — UTAGEには多数同梱されている（例: `SamplePageEvent.cs`、
   `SampleParamTrigger.cs`）。ゼロから書くより、動作実績のあるパターンをコピーする方が確実
3. フックの方式を選ぶ（どちらもUTAGE自身のサンプルに実例があるパターン、詳細はWorkflows参照）:
   - **Inspector配線**: イベントが期待するシグネチャに合う普通のメソッドを書き、
     Inspector上でそのコンポーネントの`UnityEvent`欄にドラッグする
   - **コード購読**: `Awake()`で`.AddListener(...)`を呼び、対応する`.RemoveListener(...)`を
     `OnDestroy()`で呼ぶ

期待される結果: シナリオファイルを一切変更せずに、選んだライフサイクルのタイミングで
自分のメソッドが実行される。

## Workflows

### Workflow: まず既製の任意コンポーネントを確認する

**Goal:** UTAGEが既製の任意コンポーネントとして同梱している機能を、わざわざカスタムコードで
書き直さないようにする。

**Steps:** イベントをフックしたりカスタムコマンドを書いたりする前に、このスキルフォルダ内の
`references/extra-components.md`を確認する — `Assets/Utage/Scripts/ADV/Extra/`配下の
コンポーネント一覧（ギャラリー制御、時間制限選択肢、バックログフィルタ等）をまとめており、
いずれもデフォルトではアタッチされていない。合うものがあれば
`Add Component > Utage > ADV > Extra > ...`で追加してInspectorで設定する。この参照表は
要約に過ぎないので、実ファイルのソースを先に読むこと。

**Expected result:** 既存コンポーネントの追加・設定だけで要望を満たせる（新規コード不要）か、
どれも合わないことが確認でき、以降のカスタムフック（下記）が実際に必要だと判断できる。

### Workflow: Inspector配線によるライフサイクルイベントのフック

**Goal:** コード側での購読なしに、UTAGEのInspector駆動の`UnityEvent`を使ってタイミングで
コードを実行する。

**Steps:** `Assets/Utage/Sample/Scripts/SamplePageEvent.cs`のパターンに従い、必要な
イベントのシグネチャに合う公開メソッドを持つ`MonoBehaviour`を作る（例:
`OnBeginText(AdvPage page)`、`OnEndText(AdvPage page)`）。シーン内のGameObjectに
アタッチし、対象イベントを持つコンポーネントのInspectorで、このコンポーネントを
ドラッグして該当メソッドを選択する。

**Expected result:** シナリオ再生中の該当タイミングでメソッドが発火する
（`Debug.Log`やブレークポイントで確認可能）。コード側の配線は一切不要。

### Workflow: コードでのライフサイクルイベント購読

**Goal:** Inspector配線なしに、スクリプトからライフサイクルのタイミングでコードを実行する。

**Steps:** `Assets/Utage/Sample/Scripts/SampleParamTrigger.cs`のパターンに従い、
イベントを持つコンポーネントへの参照を取得し（例: `AdvEngine.ParameterEventTrigger`、
または`onPreInit`/`OnPostInit`なら`AdvEngine`自体）、`Awake()`でそのイベントの
`AddListener(...)`を呼ぶ。必ず`OnDestroy()`で`RemoveListener(...)`と対にすること —
UTAGE自身のサンプルコメントも、特に動的に生成・破棄されるものについてこれを明示的に
警告している。

**Expected result:** イベント発火時にリスナーメソッドが実行され、オブジェクト破棄時に
きれいに解除される（シーンリロード後の二重発火が起きない）。

### Workflow: シナリオコマンドからカスタムC#コードを呼び出す

**Goal:** シナリオの1行から、自分のC#コードを実行させる。

**Steps:** これはここではなく、同梱のシナリオリファレンスで詳しく扱われている —
06章（`06_Commands_UI_Integration_ja.md`／`_en.md`）を読み、組み込みの`SendMessage`／
`SendMessageByName`／`BroadcastMessageByName`コマンドを使う。これらはシナリオの行から
直接GameObject上の指定メソッドを呼び出すもので、カスタムコマンドクラスは不要。
それでは足りない場合（全く新しいコマンド名を登録したい、既存の組み込みコマンドを
上書きしたい等）、UTAGEは`Utage.AdvCustomCommandManager`を継承し
`Utage.AdvCommandParser.OnCreateCustomCommandFromID`を購読する仕組みもサポートしている
— ゼロから書く前に`Assets/Utage/Sample/Scripts/SampleCustomCommand.cs`の実例を確認すること。

**Expected result:** シナリオ行（`SendMessage`または完全なカスタムコマンド）が、
リビルド後（`utage-write-scenario`参照）にシナリオがその行に到達した時点であなたの
C#コードを実行する。

## Verification

- フックしたメソッドを持つ`MonoBehaviour`が、実際に再生されるシーンのGameObjectに
  アタッチされていること
- Inspector配線の場合: 対象コンポーネントのInspectorの`UnityEvent`一覧に、そのメソッドが
  選択済みで表示されること
- コード購読の場合: `Awake()`の`AddListener`と`OnDestroy()`の`RemoveListener`が対になっていること
- フックが期待通りのタイミングで発火すること（ログ／ブレークポイントで確認）、
  コンソールに例外が出ていないこと

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| https://madnesslabo.net/utage/?page_id=247 （とそこからリンクされる各コンポーネントページ） | Webドキュメント | UTAGEのコンポーネントとそのイベントの一覧。数が多くここでは網羅できないため、まず確認すべき場所 |
| `Assets/Utage/Sample/Scripts/` | サンプルスクリプト | パッケージ同梱の、動作実績のあるイベント使用例（`SamplePageEvent.cs`、`SampleParamTrigger.cs`等） |
| `Assets/Utage/Scripts/ADV/Extra/`（`references/extra-components.md`参照） | 任意コンポーネント | デフォルトでは未アタッチの既製コンポーネント（ギャラリー制御・時間制限選択肢等）。カスタムコードを書く前に確認すべき |
| `Utage.AdvEngine.onPreInit` / `OnPostInit` | `UnityEvent`フィールド/プロパティ | エンジン初期化のライフサイクルフック |
| `Utage.AdvEngine.ParameterEventTrigger` | コンポーネント | パラメーター変更イベント（`OnChanged`、`AddEventByName`、`AddBoolEvent`、`AddFloatEvent`、`AddIntEvent`、`AddStringEvent`と対応する`Remove*`） |
| `Assets/Utage/Docs/ScenarioReference/{ja,en}/06_Commands_UI_Integration_*.md` | 同梱リファレンス章 | `SendMessage`／`SendMessageByName`／`BroadcastMessageByName`を解説。シナリオ**から**カスタムコードを呼ぶ際の主要手段 |

## Common issues

- **ハンドラが発火しない** — 原因（Inspector配線）: メソッドが`UnityEvent`欄にドラッグ
  されていない、または参照しているGameObject/コンポーネントが間違っている。
  原因（コード購読）: `AddListener`を呼ぶ際の参照がnullだった、または違うインスタンスを
  指していた。対処: Inspector欄を確認するか、`Awake()`内でログを出して購読が
  実際に実行されたか確認する
- **ハンドラが二重発火する／シーンリロード後にリークする** — 原因: コード購読した
  リスナーの`AddListener`に対応する`RemoveListener`が`OnDestroy()`に無い。
  対処: 必ず対称に解除する
- **どのイベント/コンポーネントを使えばいいか分からない** — 記憶で推測しないこと。
  https://madnesslabo.net/utage/?page_id=247 とそこからリンクされる各コンポーネントページを
  確認するか、`Assets/Utage/Sample/Scripts/`内に既存の実例が無いか先に探す

## Boundaries

- このスキルはUTAGEのイベント一覧を網羅しない — コンポーネント数・イベント数が多すぎる。
  具体的なイベントは常にWebドキュメントか`Assets/Utage/Sample/Scripts/`で確認し、
  メソッド名/イベント名を捏造しないこと
- シナリオ**から**カスタムコードを呼ぶ方向はここでは最低限の言及にとどめる。
  その方向の正本は同梱のシナリオリファレンス（06章）
- シナリオ内容そのものは扱わない — `utage-write-scenario`参照
- 英語話者のユーザーには`Assets/Utage/AI/Skills/`配下の英語版を案内する
