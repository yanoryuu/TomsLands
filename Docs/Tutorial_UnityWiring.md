# チュートリアル Unity 配線メモ

## 村・準備

### 会話データ（要エディタ操作）
- セリフは `Assets/TomsScenario/Scenarios/VillageTutorial.csv`（ラベル: `Tutorial_Village` / `Tutorial_VillageBuild` / `Tutorial_Preparation`）。
- 既存 CSV と同じ登録方法: `TomsScenario.project`（宴プロジェクト）の章 `Boot` は `scenarioDir` = `Assets/TomsScenario/Scenarios` フォルダを指しており、
  インポートするとフォルダ内の各 CSV から `<名前>.book.asset` が作られ、`Boot.chapter.asset` の `dataList` に並ぶ
  （`TomsScenario.scenarios.asset` は `Boot.chapter` を参照するだけ）。
- 手順:
  1. `Assets/TomsScenario/TomsScenario.project.asset` を選択 → Inspector の **Import**（または宴メニューのシナリオインポート）を実行。
  2. `VillageTutorial.book.asset` が生成され、`Boot.chapter.asset` の `dataList` に入ったことを確認。
  3. インポート時にエラー（キャラ名・表情名・`CharacterLayer` の不一致）が出ていないか Console を確認。
- 登場キャラ: トコ（02_笑顔/03_大笑い/08_驚き/10_真剣）、占い師（01_通常/02_笑顔/12_ドヤ顔）。いずれも `Character.csv` に定義済み。

### ScenarioPlayer（シーン配置は不要）
- `ScenarioSystemLoader`（`RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`）が Addressables の `ScenarioSystem` プレハブを
  生成して DontDestroyOnLoad で常駐させるため、VillageScene / PreparationScene から起動しても `ScenarioPlayer` は存在する。
  シーンへの追加配置は不要。
- 会話UIは宴の Overlay キャンバス（SortingOrder 2000）、立ち絵は宴カメラ（シーンの Base カメラのスタックに Overlay として積まれる）で描かれる。

### コード側（配線済み・追加作業なし）
- `VillageLifetimeScope` / `PreparationLifetimeScope` に `TutorialScenarioService` を登録（既読は `MetaProgressModel` = `slot_N/metaData.json`）。
- 村: `VillagePresenter.Start` → 未読なら `VillageTutorialDirector.PlayAsync`。
  - 前半 `Tutorial_Village` は暗幕（ランタイム生成の Screen Space - Camera キャンバス。村 HUD を描くカメラ／無ければ Camera.main、
    最前面ソーティングレイヤー・Order 32000）で村を隠して再生 → 暗幕を 0.9 秒でフェードアウト（＝「トコの村へ」の背景切り替え）→ 後半 `Tutorial_VillageBuild`。
  - 会話中: `PlayerInput.DeactivateInput` + `PlayerMove` 無効化、村HUDのルートキャンバスの `CanvasGroup.blocksRaycasts=false`、
    Presenter 側でも区画の調べる・投資・出店・タイトルを無視。終了2フレーム後に解除。
  - 帰還報告（VillageArrivalReport）がある場合は会話のあとに到着演出を再生。
- 準備: `PreparationPresenter.Start` → `Tutorial_Preparation` が未読なら再生。会話中は準備画面のルートキャンバスの
  `CanvasGroup.blocksRaycasts=false` と、出店／戻るの無視で操作をロック。
- ラベル未インポート時: 村は暗幕を即消して通常どおり、準備は何もしない（既読にもならない）。

### 確認方法
- スロットの `metaData.json` の `tutorials` から上記3ラベルの要素を手で消すと再生し直せる（デバッグメニューにリセット機能は無い）。
- 村: 暗幕の上に立ち絵とセリフが出ること（立ち絵が暗幕に隠れる場合は、村HUDキャンバスが Screen Space - Camera か、
  `VillageView.UiCamera` が村の Base カメラを返しているかを確認）。

## 店・鍛冶屋（通常営業チュートリアル／10/12台本）

### 会話データ（要エディタ操作）
- `Assets/TomsScenario/Scenarios/Tutorial.csv` を更新済み。上と同じ手順で再インポートして `Tutorial.book.asset` に反映する。
  - 変更: `Tutorial_Blacksmith` の最終行（「それじゃあ手持ちのお金、全部使って『オート購入』してみようっと！」）。
  - 追加: `Tutorial_FundsShortage`（店・2日目の朝）/ `Tutorial_FundsAdvance`（鍛冶屋・入金ルール〜立て替え）/
    `Tutorial_FundsAdvanceAfter`（鍛冶屋・需要と価格の見方）。
  - 鍛冶屋の表情 04_困り / 12_ドヤ顔 を新たに使う（`Character.csv` に定義済み）。

### 進行（コード側は配線済み）
| 台本の▼ | 実装 |
|---|---|
| 「初めから」で直接開店 | `TitlePresenter.StartNewGame`: スロットの `Tutorial_Opening` が未読なら VillageScene ではなく TomsShop をロード（難易度=ふつう、準備設定なし） |
| 鍛冶屋以外クリック不可 | `TutorialPresenter.UpdateShopLock` → `TomsShopView.SetTutorialBlacksmithOnly` + `TurnPhaseView.SetAdvanceLocked`（初回オート購入が済むまで。鍛冶屋ボタンは脈動） |
| オート購入で全額消費 | `BlackSmithPresenter`: `Tutorial_Blacksmith` 終了で予算ポップアップを自動表示（予算=所持金全額・入力/全額/半分/方針/キャンセル不可、「購入」だけ押せる）。それまで閉じる・タブ・並べ替え・一覧・詳細パネルはロック |
| 売上リザルト後の会話 | 2ターン目の店入場で `Tutorial_FundsShortage` → 終了後に自動で鍛冶屋へ遷移 |
| 立て替え資金 | `Tutorial_FundsAdvance` 終了時に `TutorialSteps.FundsAdvanceAmount`(50,000G) を加算・保存（段階フラグ `TutorialStep_FundsAdvanceGranted` で一度きり） |
| 需要高×価格安の強調 | `Tutorial_FundsAdvanceAfter` 終了で `BlackSmithModel.TutorialBargainHighlight=true` → スコア（需要÷(現在価格/基準価格)）上位最大3件の行を脈動＋背景明滅、最上位のタブを開いて選択。鍛冶屋を閉じると解除 |
| 占い師との出会い | `Tutorial_ProphetMeeting` を 2ターン目→3ターン目（2日目の営業後）へ移動 |

### 任意の配線
- `ItemShopSlot`（鍛冶屋の行プレハブ）に `tutorialHighlight`（強調枠の GameObject、初期非アクティブ）を追加できる。
  未配線でも行の拡縮と `rowBackground` の色の明滅で強調される。
- シーン・共有プレハブ・GameLifetimeScope の変更は不要（`TutorialPresenter` の新しい依存 `TomsShopView` / `TurnPhaseView` /
  `BlackSmithModel` / `TomsModel` はすべて登録済み）。

### 確認方法
- 新しいスロットで「初めから」→ 店で冒頭会話 → 鍛冶屋以外が押せない → 鍛冶屋で会話後にロックされた予算ポップアップ → 購入 →
  店へ戻る → 陳列 → 営業 → リザルト確認 → 2日目の朝に会話 → 自動で鍛冶屋 → 会話 → +50,000G → 会話 → おすすめ行が光る。
- やり直すときは `metaData.json` の `tutorials` から `Tutorial_*` と `TutorialStep_*` の要素を消す（スロット削除でも可）。
