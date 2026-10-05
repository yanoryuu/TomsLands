---
name: utage-setup-and-overview
description: "Use this skill whenever the user wants to set up, install, verify, or get oriented with UTAGE (宴/Utage) in a Unity project — e.g. 'UTAGEをセットアップして', 'UTAGEで新しいプロジェクトを作りたい', 'UTAGEでノベルゲームを始めたい', '既存のシーンにビジュアルノベルを追加したい', 'UTAGEが正しくインストールされているか確認して', 'UTAGEのシナリオファイルはどこにある？'. Covers first-time project setup via the New Project window (creating a new ADV scene, adding UTAGE to an existing scene, or creating a scenario-only project), verifying the asset is installed, and locating key files (scenario data, scripts, samples, docs). Do NOT use for writing, rebuilding, or debugging scenario content (see utage-write-scenario), resource conversion (see utage-resource-management), or code-level extension via custom commands (see utage-extend-with-code). When in doubt whether a Unity visual-novel/ADV request could involve UTAGE, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# UTAGEのセットアップと概要把握

現在のUnityプロジェクトにUTAGEが正しくインストールされているかをAIエージェントが確認し、
UTAGE標準の「New Project」ウィザードで新しいビジュアルノベル（ADV）プロジェクト／シーンを
作成し、次に必要な主要ファイルやドキュメントの場所を案内するためのスキル。

## When to use this skill

- ユーザーがUTAGE／ビジュアルノベル／ADVシーンの「セットアップ」「インストール」「追加」
  「開始」を頼んできたとき
- UTAGEが正しくインストールされているか、ファイル・ドキュメントがどこにあるかを聞かれたとき
- 新規UTAGEプロジェクトを作りたい、または既存シーンにUTAGEを追加したいとき
- Not for: シナリオ内容の記述・リビルド・デバッグ（`utage-write-scenario`参照）、
  画像/音声リソースの変換（`utage-resource-management`参照）、
  C#によるUTAGEの拡張（`utage-extend-with-code`参照）

## Prerequisites

- Unity 6000.0.58f2以降
- Built-in Render PipelineまたはUniversal Render Pipeline（URP）— HDRP対応は未確認
- UTAGE（本メモ執筆時点でバージョン4.2.9）が`Assets/Utage/`配下にインポート済みであること

**プログラム的なインストール確認方法** — 進める前に以下のどちらかを確認する:
- 型`Utage.AdvEngine`がプロジェクト内に存在する（`Assets/Utage/Scripts/ADV/AdvEngine.cs`で定義）
- Unity Editorのメニューに`Tools > Utage > New Project`が存在する

確認に失敗した場合、UTAGEが未インポート（または一部のみインポート）の状態。
以下のAsset Storeページからパッケージをインポートするようユーザーに伝える:
https://assetstore.unity.com/packages/tools/game-toolkits/utage4-for-unity-text-adventure-game-engine-version4-266447

## Quick start

UTAGEで動作するシーンを最短で用意する方法は、UTAGE標準の「New Project」ウィザードを使うこと。
シーンをゼロから手作業で組み立てないこと。

1. Unity Editorで`Tools > Utage > New Project`を開く
2. "Input New Project Name"に、プロジェクト名を入力する
   （空欄不可。`Assets/<name>/`が既に存在する場合はCreateボタンが無効のまま）
3. "Select Create Type"で**Create New Adv Scene**を選択する（他の2種類はWorkflows参照）
4. ユーザーから特定のテンプレート指定がなければ、"Template Settings"はデフォルトのままにする
   （デフォルトは "New Scene Default Settings ... New Scene Default TMP"）
5. "Create Settings"欄（選択したTemplate Settingsによって表示される項目が変わる。
   TMP/URPベースのテンプレートでは以下が全て表示される）:
   - **Secret Key** — デフォルトは文字通りのプレースホルダー文字列`InputOriginalKey`。
     セーブデータとシナリオファイルの暗号化キーとして使われる
     （`FileIOManager.SetCryptKey`）。**必ずプロジェクト固有の実際のキーに書き換えること** —
     プレースホルダーのままだと「暗号化」キーが公開されているのと同じになる。空欄だと
     Createが無効のまま
   - **Game Screen Width / Game Screen Height** — デフォルト`1280`／`720`。ゲームの基準解像度
     （`LetterBoxCamera`と`ScreenResolution`に適用される）。両方とも0より大きい必要がある
   - **Auto Clear Urp Volumes** — チェックボックス、デフォルトON。URP使用時のみ表示。
     チェックすると、プロジェクト作成時にデフォルトの`UniversalRenderPipelineAsset`に
     設定済みのVolumesをクリアする
   - **Font Language** — ドロップダウン、TextMeshProベースのテンプレートのみ表示。
     デフォルトはEditorのシステム言語（例: "Japanese"）。空欄不可。
     シナリオ本文の執筆言語に合わせて選ぶことで、正しいTMPフォントが適用される
6. **Create**をクリックする

期待される結果: `Assets/<ProjectName>/`という新しいフォルダが作成される。中身は以下の通り:
- `<ProjectName>.unity` — 新規シーン。Editorで自動的に開かれ、ルートに`AdvEngine`
  GameObject（と`UI/MessageWindowManager/...`配下の子オブジェクト群）、上記で入力した
  Secret Keyを使う`FileIOManager`、Game Screen Width/Heightに合わせたサイズの
  `LetterBoxCamera`／`ScreenResolution`が含まれる
- `Scenarios/<ProjectName>.xls` — シナリオデータのテンプレート（Excel）と、
  それをインポートした`<ProjectName>.book.asset`・`<ProjectName>.scenarios.asset`
- `Fonts & Materials/` — 対応言語ごと（NotoSans、NotoSansJP、NotoSansKR、NotoSansSC、
  NotoSansTC）に生成されたTextMeshProフォントアセットと対応するマテリアル
- `Audio/<ProjectName> AudioMixer.mixer`と`Resources/<ProjectName>/`フォルダ

コンソールにエラーが出ないこと（実行中に出る"RebuildAssets･･･"／"...End RebuildAssets"という
情報ログは正常な範囲）。

## Workflows

### Workflow: 新規ADVシーンプロジェクトの作成

**Goal:** UTAGEで作るビジュアルノベル専用の、まっさらな新規シーンを開始する。

**Steps:** 上記のQuick startと同じ手順。手順3のCreate Typeは**Create New Adv Scene**を選択。

**Expected result:** `Assets/<ProjectName>/`配下に、UTAGEエンジンが初期化済みの新規シーン
ファイルと、コピーされたテンプレートのシナリオデータアセットが生成され、コンソールに
エラーが出ない。

### Workflow: 既存シーンへのUTAGE追加

**Goal:** 新しい専用シーンを作るのではなく、ユーザーが既に持っている既存シーン
（既存のゲームプレイシーン等）にUTAGEを組み込む。

**Steps:** まず対象の既存シーンを開いてから、Quick startの手順を実行する。ただし手順3の
Create Typeは**Add To Current Scene**を選択する。

**Expected result:** UTAGEのエンジンとテンプレートのシナリオデータアセットがプロジェクトに
追加され、新規シーンファイルではなく現在開いているシーンに組み込まれる。

### Workflow: シナリオ専用プロジェクトの作成

**Goal:** シーンには一切触れず、シナリオデータアセット（Excel/CSVベース）だけを用意する。
シーン側は別途用意する場合や、シーン作業より先にシナリオ執筆を始めたい場合に有用。

**Steps:** Quick startの手順を実行する。ただし手順3のCreate Typeは
**Create Scenario Asset Only**を選択する。

**Expected result:** `Assets/<ProjectName>/`配下にテンプレートのシナリオデータアセットの
コピーが作成される。シーンは作成も変更もされない。

## Verification

- `Assets/<ProjectName>/`が存在し、UTAGEのテンプレートアセットのコピーを含んでいること
- 「Create New Adv Scene」の場合: そのフォルダ配下に新しい`.unity`シーンファイルが存在し、
  それが現在開いているシーンになっていること。階層のルートに`AdvEngine`が存在すること
- `Scenarios/<ProjectName>.xls`（シナリオデータのテンプレート）が存在すること
- New Projectウィンドウの Create ボタンをクリックした後、Unityコンソールにエラーが
  出ていないこと（"Failed save scene"エラーはシーンの保存に失敗したことを示すので、
  ユーザーに報告する）
- 入力したSecret Keyが、デフォルトの文字列`InputOriginalKey`のまま残されていないこと
  （Common issues参照）

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools > Utage > New Project` | Editorメニュー項目 | 新規ADVシーンの作成、既存シーンへのUTAGE追加、シナリオ専用プロジェクトの作成を行うウィザードを開く |
| `Utage.AdvEngine` | クラス（`Assets/Utage/Scripts/ADV/AdvEngine.cs`） | ADVシナリオの再生を駆動するランタイムエンジン。シーン/プロジェクト内での存在確認が、上記のインストール／初期化チェックに使われる |
| `IAdvProjectCreatorSecurity.SecretKey` | インターフェースプロパティ | Create Settingsの"Secret Key"欄。ここで設定した値がセーブデータとシナリオファイルの暗号化キーになる |
| `IAdvProjectCreatorGameScreenSize.GameScreenWidth/Height` | インターフェースプロパティ | Create Settingsの"Game Screen Width/Height"欄。プロジェクト作成時に`LetterBoxCamera`と`ScreenResolution`へ適用される |

## Common issues

- **Createボタンが無効のまま押せない** — 原因: プロジェクト名が空欄、
  `Assets/<name>/`に既にフォルダが存在する、Secret Keyが空欄、Game Screen Width/Heightが
  0以下、またはTMPテンプレートでFont Languageが空欄。対処: Create Settingsの全項目を
  埋める。空欄でない、まだ使われていないプロジェクト名を指定する
- **コンソールに"TemplateSettings is invalid"／"Failed Create settings"と出る** —
  原因: 選択中のTemplate Settingsアセットが欠落または不正な設定になっている。
  対処: Template Settingsをデフォルトのままにするか、ユーザーに意図したカスタム
  テンプレートを確認する
- **コンソールに"Failed save scene"と出る** — 原因: 新規シーンをディスクに保存できなかった
  （権限不足・ディスク容量不足等）。対処: コンソールに出たエラーをそのままユーザーに報告する。
  黙ってリトライすべきではない
- **Secret Keyがデフォルトの`InputOriginalKey`のまま** — コンソールエラーにはならないが
  実質的な落とし穴。この欄はランダムなキーではなく、文字通りのプレースホルダー文字列が
  デフォルトで入っている。変更せずにプロジェクトを配布すると、セーブ/シナリオの
  「暗号化」キーが事実上公開されているのと同じになる。セットアップ完了前に必ず
  ユーザー自身のキーに変更するよう促すこと

## Skill index

- `utage-setup-and-overview`（このスキル） — インストール確認、New Projectウィザード、主要ファイルの場所
- `utage-write-scenario` — セリフ・選択肢・コマンドといったシナリオ内容の記述・編集、
  編集したシナリオデータのリビルド、インポートエラーの読み方、再生状態の確認
- `utage-resource-management` — AssetBundleのパッケージ化とダイシング変換（上級者向け・任意）
- `utage-extend-with-code` — C#によるカスタムシナリオコマンドとエンジンライフサイクルフック

## Boundaries

- このスキルは公式のNew Projectウィザードでプロジェクトを開始する範囲のみを扱う。
  シナリオスクリプトの記法、リソース変換、C#による拡張は扱わない — 上記のSkill index参照
- UTAGEインポート後、レンダーパイプライン（URP等）やUnityバージョンの不一致に関連する
  エラーが出た場合、UTAGE自身に対応する内部互換性機構がある —
  `Tools > Utage > Extension Package Manager`が、現在のパイプライン/Unityバージョンに
  必要な追加アセットをインポートする。これはトラブルシューティング用のフォールバックであり、
  サードパーティ拡張のための汎用パッケージマネージャーではない
- 英語話者のユーザーには`Assets/Utage/AI/Skills/`配下の英語版（同じ構成・英語の本文）を
  案内する
- HDRP対応は未確認。ユーザーに確認を取らずにHDRP環境での動作を前提にしないこと
