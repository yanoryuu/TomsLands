# タイトル セーブデータ ロード（3スロット）— Unity配線手順

タイトル画面で「続きから」「ニューゲームの保存先選択」「セーブデータ削除」を
最大3スロットで行う仕組みを追加した。スクリプトの実装は完了しているので、
このドキュメントの通りに **Inspector / Prefab / Scene の配線** を行えば動作する。

## 概要（仕組み）

- 全セーブファイル（`save.json` / `tomsData.json` / `itemData.json` / `heroData.json` /
  `shopStatusData.json` / `streamingSelection.json`）は
  `persistentDataPath/slot_N/`（N=0,1,2）配下に保存されるようになった。
- 選択中スロットは `SaveSlotManager.CurrentSlot`（PlayerPrefsに永続化）で管理。
  タイトルでスロットを確定 → シーン遷移しても保持される。
- タイトルのロードパネルは **プレハブ1つを動的に3個生成** して各スロットを表示する。

## 1. スロット用プレハブを作る（`SaveSlotView`）

1スロット分のUIプレハブを作成し、ルートに **`SaveSlotView`** をアタッチする。
プレハブ内に以下を用意して、`SaveSlotView` の各フィールドへ割り当てる。

| フィールド | 役割 | 必須 |
|---|---|---|
| `selectButton` (Button) | スロット本体の選択（ロード／上書き先指定） | ◯ |
| `deleteButton` (Button) | このスロットを削除 | 任意 |
| `slotNumberText` (TextMeshProUGUI) | 「スロット 1」などの見出し | 任意 |
| `summaryText` (TextMeshProUGUI) | 「ふつう Day 5 1,200 G」などのサマリ | 任意 |
| `emptyLabel` (GameObject) | 空きスロット時に表示する「空き」など | 任意 |
| `filledGroup` (GameObject) | データあり時に表示するまとまり（サマリ＋削除ボタン等） | 任意 |

ポイント:
- `selectButton` はスロット全体を覆う透明ボタンにすると押しやすい。
- `deleteButton` は `filledGroup` の中に置くと「データありのときだけ表示」になる
  （`Bind()` 内で `filledGroup` の表示/非表示を切り替えるため）。

## 2. TitleView に割り当てる

`TitleView`（タイトルシーンのオブジェクト）の Inspector、
**「セーブデータ選択画面」** セクションに以下を割り当てる。

| フィールド | 割り当てるもの |
|---|---|
| `saveSlotPrefab` | 手順1で作った `SaveSlotView` プレハブ |
| `saveSlotContainer` | スロットを並べる親Transform（`Vertical/Horizontal Layout Group` 推奨） |
| `loadModeHeader` | 「続きから」見出しオブジェクト（任意） |
| `newGameModeHeader` | 「保存先を選んでください」見出しオブジェクト（任意） |
| `saveDataBackButton` | 既存の戻るボタン（変更なし） |

> 旧 `saveDataSlotButton`（単一スロット用ボタン）フィールドは削除した。
> Inspectorに「Missing」表示が残る場合があるが無視してよい。
> 旧ボタンのGameObjectはシーンから外す or `saveSlotContainer` 用に流用する。

`saveSlotContainer` の子に手動でスロットを置く必要はない（実行時に動的生成される）。
デザイン確認用に置いたダミーがあれば消しておくこと（生成分と重なるため）。

## 3. 動作フロー（確認用）

- **続きから**: タイトル → 「続きから」→ ロードパネル（Loadモード）。
  データのあるスロットだけ選択可。選ぶと `CurrentSlot` を確定してそのスロットをロード。
- **ニューゲーム**: 「ニューゲーム」→ 難易度選択 → 難易度確認ポップアップ →
  保存先スロット選択（NewGameSlotモード、全スロット選択可）。
  使用中スロットを選ぶと「上書きの確認」ポップアップ → 確定で新規開始。
- **削除**: 各スロットの `deleteButton` → 「削除の確認」ポップアップ → 確定で
  そのスロットのフォルダを丸ごと削除し、一覧を再描画。

## 4. 既存セーブデータについて

- 旧形式（`persistentDataPath` 直下のJSON）は新スロット構造では参照されない。
  エディタの **Tools > TomsLands > デバッグ > 全セーブデータ削除** が旧形式＋全スロットを掃除するよう更新済み。
- 開発中の動作確認では一度クリアしてから始めると分かりやすい。
