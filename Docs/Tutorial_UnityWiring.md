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
- 2026-10（K）: `Tutorial.csv` と `CharacterIntro.csv` を変更し、`Tutorial.book.asset` / `CharacterIntro.book.asset` は再インポート済み（バッチで `AdvScenarioDataProject.ImportAll()`）。
  CSV をさらに直したら、エディタで宴の「シナリオをインポート」（またはプロジェクト `Assets/TomsScenario/TomsScenario.project.asset` の Import）をやり直す。
  - 追加: `Tutorial_OpeningShop` / `Tutorial_HeroEquip` / `Tutorial_RelicFirst` / `Tutorial_StreamingEndHard` / `Intro_Newspaper`
  - 変更: `Tutorial_FundsShortage`（良い知らせ）/ `Tutorial_FundsAdvance`（叱らない。「並べた次の日の夜に入金、あさってから使える」）/ `Tutorial_FundsAdvanceAfter`（新聞の2行）/
    `Tutorial_ProphetMeeting` の最後（明日の配信）/ `Tutorial_StreamingPrepare`（在庫から選ぶ）/ `Intro_Hero` 1行目 / `Intro_DemonLord` 1行目（占い師の案内は無い）
  - 口調は Docs の付録の基準（鍛冶屋「オレ」、魔王「我」、占い師「ボク・キミ・トコちゃん」）。主人公は「トコ」。
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
| オート購入で全額消費 | `BlackSmithPresenter`: `Tutorial_Blacksmith` 終了で予算ポップアップを自動表示（予算=所持金全額・入力/全額/半分/キャンセル不可。**方針だけは選べる**（K-5、既定はおすすめ順）、あとは「購入」だけ押せる）。それまで閉じる・タブ・並べ替え・一覧・詳細パネルはロック |
| 売上リザルト後の会話 | 2ターン目の店入場で `Tutorial_FundsShortage`（「昨日の注文がぜんぶ入った、代金は今夜」＝良い知らせとして。K-3）→ 朝の通知を出し終えてから自動で鍛冶屋へ遷移 |
| 立て替え資金 | `Tutorial_FundsAdvance` 終了時に `TutorialSteps.FundsAdvanceAmount`(50,000G) を加算・保存（段階フラグ `TutorialStep_FundsAdvanceGranted` で一度きり） |
| 需要高×価格安の強調 | `Tutorial_FundsAdvanceAfter` 終了で `BlackSmithModel.TutorialBargainHighlight=true` → スコア（需要÷(現在価格/基準価格)）上位最大3件の行を脈動＋背景明滅、最上位のタブを開いて選択。鍛冶屋を閉じると解除 |
| 占い師との出会い | `Tutorial_ProphetMeeting` は **初配信の前日の朝**（3ターン目以降で、次の配信まで1日）に出す（K-6）。出会いそびれた場合は配信の当日・配信後に拾う |
| 勇者に何を持たせるか（K-1） | 出会いの直後に `Tutorial_HeroEquip`（占い師「勝てば持たせた品は値上がり、ボスならなおさら。負ければ下がる」）→ 朝の通知を出し終えてから勇者画面を自動で開く |
| 新聞の読み方（K-2） | `Tutorial_FundsAdvanceAfter` の最後に鍛冶屋の2行（先読みは新聞）。新聞を初めて開いたとき `Intro_Newspaper`（CharacterIntro.csv。5社・記事→属性→値動き・外れる記事・貼る） |
| プロローグ（K-5） | `Tutorial_Opening`（暗幕のまま〜「開店だよー！」）→ 幕を開けて `Tutorial_OpeningShop`（棚の商品が時代遅れ〜） |
| 初配信の後日談（K-8） | 勇者が勝っていれば `Tutorial_StreamingEnd`、負けていれば `Tutorial_StreamingEndHard`（「思ったより難しいね」「勝ち負けを読んで張れば」）。片方を見たらもう片方も既読にする。判定は `RunHistory.streamWins` |
| レリックの説明（K-10） | 最初のレリック3択を出す直前に `Tutorial_RelicFirst`（TomsShopPresenter） |
| イベント・バズの説明（K-9） | `TurnEvent_EventTutorial` は初めてイベントが起きたとき、`TurnEvent_BuzzTutorial` は初めて（ふつうの）バズが起きたとき。どちらも占い師に出会った後だけ。以前の「初配信の2日後／5日後に無条件」はやめた |
| ボタンを段階的に開ける（K-7） | 1周目だけ `TutorialPresenter.ApplyStagedUnlock`: 1日目 鍛冶屋・陳列・返済 / 2日目 新聞 / 占い師に出会ったら 占い師・情報屋・地図・勇者 / 初配信のあと 魔王軍支援・広告 / 最初の返済か初配信の2日後 店（改装・設備はタブ）。新しく出たボタンは一度だけ弾む（段階フラグ `TutorialStep_Open_*`）。2周目以降は全部出す |
| 始めるまでのクリック（K-13） | どのスロットにもデータが無いとき（`SaveSlotManager.AnyProfileExists()==false`）、「初めから」はスロット選択を飛ばしてスロット1で始める |

### 任意の配線
- `ItemShopSlot`（鍛冶屋の行プレハブ）に `tutorialHighlight`（強調枠の GameObject、初期非アクティブ）を追加できる。
  未配線でも行の拡縮と `rowBackground` の色の明滅で強調される。
- シーン・共有プレハブ・GameLifetimeScope の変更は不要（`TutorialPresenter` の新しい依存 `TomsShopView` / `TurnPhaseView` /
  `BlackSmithModel` / `TomsModel` はすべて登録済み）。

### 確認方法
- 新しいスロットで「初めから」→ 店で冒頭会話 → 鍛冶屋以外が押せない → 鍛冶屋で会話後にロックされた予算ポップアップ → 購入 →
  店へ戻る → 陳列 → 営業 → リザルト確認 → 2日目の朝に会話 → 自動で鍛冶屋 → 会話 → +50,000G → 会話 → おすすめ行が光る。
- やり直すときは `metaData.json` の `tutorials` から `Tutorial_*` と `TutorialStep_*` の要素を消す（スロット削除でも可）。
