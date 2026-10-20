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
  データのあるスロット（進行中のラン or 村の進行）だけ選択可。選ぶと `CurrentSlot` を確定し、
  進行中のランがあればそのスロットをロード、村の進行だけなら村へ（§5）。
- **ニューゲーム**: 「ニューゲーム」→ 難易度選択 → 難易度確認ポップアップ →
  保存先スロット選択（NewGameSlotモード、全スロット選択可）。
  進行中のランがあるスロットを選ぶと「上書きの確認」ポップアップ（消えるランの中身を表示）→ 確定で新規開始。
- **削除**: 各スロットの `deleteButton`（村の進行だけ・壊れたスロットでも押せる）→ 「削除の確認」ポップアップ → 確定で
  そのスロットのフォルダを丸ごと削除し、一覧を再描画。

## 4. 既存セーブデータについて

- 旧形式（`persistentDataPath` 直下のJSON）は新スロット構造では参照されない。
  エディタの **Tools > TomsLands > デバッグ > 全セーブデータ削除** が旧形式＋全スロットを掃除するよう更新済み。
- 開発中の動作確認では一度クリアしてから始めると分かりやすい。

## 5. スロットの表示（4通り）と「続きから」の行き先

`SaveSlotView.Bind` は `SaveSlotInfo` から次の4通りを出し分ける（プレハブの配線は変更なし）。

| 状態 | 判定 | 表示（summaryText） | 削除 | 続きから |
|---|---|---|---|---|
| 進行中のラン | `Exists`（tomsData.json が読める） | 「ふつう　Day 5　1,200 G」 | ◯ | TomsShop |
| 村の進行だけ（ラン終了後） | `HasProfile && !Exists`（metaData.json だけ） | 「村の進行あり　村資金 45,000 G　3周」 | ◯ | **VillageScene** |
| 壊れている | `IsCorrupted`（本体も .bak も読めない） | 「データが壊れています（削除できます）」 | ◯ | 選べない |
| 空き | 上のどれでもない | 「空き」（emptyLabel） | × | 選べない |

- `SaveSlotManager.Exists` / `AnyExists` の意味は「進行中のランがあるか」のまま変えていない
  （上書き確認・続きからのロード経路・AutoPlay が使っている）。
  タイトルの「続きから」ボタンを押せるかは `AnyProfileExists`（村の進行だけのスロットも含む）で判定する。
- ニューゲームで進行中のランがあるスロットを選ぶと、上書き確認に「進行中のラン（難易度・日数・所持金）は村に戻らずに消え、
  村から持ち込んだお金も戻らない」ことを出す（村の進行は残る）。

## 6. 中断した場面からの再開（続きから）

`GameLifecycleHandler.Start` が index を復元した後、店の Entry を出す前に `GameFlowManager.TryResumeInterruptedScene()` を呼ぶ。

| 前回終了したところ | 保存されている index | 続きからの行き先 |
|---|---|---|
| 配信中（FightScene）／配信前の寄り道（鍛冶屋）中 | 配信ノード | **FightScene でその配信を最初からやり直す**。日送り・経済更新は配信前に済んで保存済みなので NextTurn は呼ばない |
| リザルト画面（精算前） | 最終ノードの先 / End ノード | ResultScene |
| イベントのポップアップ表示中 | 次の店の日 ＋ `tomsData.pendingEventId` | 店で同じイベントをもう一度出す（確認ボタンで消える） |
| 選ぶ前のレリック3択 | `relics.json` の `pendingChoices` | 店で同じ3択をもう一度出す |
| バズの途中 | `shopStatusData.json` の hasBuzz / buzzType / 残りターン / 売上倍率 | バズを続ける（効果データ SO は種類から引き直す） |

- 配信から結果を持って帰ったとき（`BattleOutputData.HasResult`）は従来どおり `BattleResultHandler` が処理する。
- 設定画面の「タイトルへ」は確認ポップアップを挟む（設定画面を閉じてから店の PopUpManager で聞く）。配信中（FightScene）は出さない。

## 7. セーブファイルの書き方（全ファイル共通: `SaveFile`）

- 書き込み: `SaveFile.WriteAtomic` … `xxx.json.tmp` に書いて `File.Replace` で本体と置き換え、直前の本体は `xxx.json.bak` に残す。
  失敗しても例外を投げずにログ（LogError）を出して false を返す（1回の失敗で以後の保存や日送りが止まらない）。
  ウイルス対策ソフトなどが一瞬掴んでいる場合に備えて2回まで再試行する。
- 読み込み: `SaveFile.TryReadJson` … 本体が壊れていれば（途中で切れた JSON・0バイト）`.bak` から復旧して本体へ書き戻す。
  どちらも読めなければ false を返し、各モデルは「セーブなし」と同じ既定値で起動する。壊れた本体は `xxx.json.corrupt` に写して残す。
  本体が無いときは `.bak` があっても読まない（ラン削除で本体だけ消えた状態を「データなし」として扱う）。
- 削除: `RunSaveCleaner` は本体と一緒に `.tmp` / `.bak` / `.corrupt` も消す（残すと次のランが前のランへ復旧しかねない）。
- 日送りの区切り: 店の日の `NextTurn` の最後に、新しい日の index を入れて全モデルを保存し、tomsData を最後に書く
  （以前は index が `GameLifecycleHandler.Dispose` でしか書かれず、店の1日の間に落ちると同じ日送りが二重に走りえた）。
- 版番号: `tomsData.json` と `metaData.json` に `saveVersion`（`SaveVersion.Current`、今は 1）を入れる。
  版を入れる前のセーブは欠損 → 0 として読む。中身の意味を変えたら `SaveVersion.Current` を上げて `SaveVersion.Migrate` に読み替えを足す。
- 二重起動: Player Settings の Force Single Instance を ON（同じスロットに2つのプロセスが書き込むのを防ぐ）。
