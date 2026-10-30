# 占い師「上げる枠・下げる枠」— 画面と仕組みの口（wt-04 への引き継ぎ）

2026-10-30。占い師は「先を読む」場所から「未来に干渉する」場所になった。
担当の分け方（ユーザーの指示）: **画面は wt-05、仕組みと AI は wt-04。**
画面は `IProphetIntervention` だけを見る。仕組みはこれを実装して、DI の登録 1 行を差し替える。

見本と UI/UX の決まり: `scratchpad/prophet3/index.html`（コーディネーターが保持）。

## 決まった仕様

| 項目 | 内容 |
|---|---|
| 枠 | **▲ 上げる枠（緑）・▼ 下げる枠（赤）が 1 つずつ。** 品のカードをどちらかの枠に入れると、その品に干渉する。同時に効くのは上げる 1・下げる 1 |
| 同じ品 | 同じ品を両方の枠には入れられない |
| 出し入れ | 入れたカードは効果が切れるまで出し入れ不可（画面は鍵・残りターンの目盛り） |
| 効果 | 需要の**均衡点**をずらす（上げる＝上へ・下げる＝下へ）。**切れたら元の需要へ戻ろうと動く**（バイアスが消えるだけ。反動の上乗せはしない） |
| 値段 | 値段は 1 つ（`CurrentPrice`）なので、仕入れ・売りの両方に効く（「下げて安く仕入れ、切れて戻ってから売る」が成り立つ） |
| 長さ | 入れた日から **3 日（仮）**。1 ターン＝1 日（朝の更新ごとに 1 減る）。0 になった朝に枠から外れる |
| 占い師の力 | 入れるたびに 1 つ使う。**最大 2。配信を 1 回終えると（途中終了でも）+1。ラン開始時は 2** |
| 代金 | 仮 15,000G（取らないなら `Cost = 0`）。村の酒場の値段（`VillageEffects.ApplyProphetPrice`）を通すかは仕組み側で決める |
| 数値の置き場 | 仕組み側のコードの 1 か所（後でマスタへ） |
| 廃止 | 「15,000G で 3 日先を占う」（`ProphetFortune`）と、占い師の画面の 3 件だけの一覧（荒れ相場・高値・おすすめ）。画面からは外した。コードの削除は仕組み側 |

## インターフェイス（`Assets/Scripts/Prophet/IProphetIntervention.cs`）

```csharp
public enum ProphetDirection { Up = 1, Down = -1 }

public enum ProphetPlaceResult
{
    Ok = 0,                  // 入れられる（TryPlace なら入れた）
    NoPower = 1,             // 占い師の力が 0
    NotEnoughMoney = 2,      // 代金が足りない
    SlotInUse = 3,           // その枠はもう使っている（切れるまで出し入れ不可）
    SameItemInOtherSlot = 4, // もう片方の枠に同じ品
    NotUnlocked = 5,         // まだ解放されていない品
    UnknownItem = 6,         // 知らない品 id・空の id
}

public readonly struct ProphetSlotState { string ItemId; ProphetDirection Direction; int TurnsLeft; int TotalTurns; }

public interface IProphetIntervention
{
    int Power { get; }          // 0〜MaxPower
    int MaxPower { get; }       // 2
    int Cost { get; }           // 1 回の代金（取らないなら 0）
    int EffectTurns { get; }    // 入れたときに効く日数（仮 3）
    ProphetSlotState? GetSlot(ProphetDirection direction);   // 空なら null
    Observable<Unit> Changed { get; }                         // R3。状態が変わったら流す
    ProphetPlaceResult CanPlace(string itemId, ProphetDirection direction);  // 何も変えない
    ProphetPlaceResult TryPlace(string itemId, ProphetDirection direction);  // Ok なら力と代金を引いて保存・Changed
}
```

各メソッドに期待する振る舞い:

- `CanPlace` の判定の順（画面のセリフが自然になる順）: 空の id・知らない品 → `UnknownItem`、未解放 → `NotUnlocked`、その枠が使用中 → `SlotInUse`、もう片方と同じ品 → `SameItemInOtherSlot`、力 0 → `NoPower`、お金 → `NotEnoughMoney`。仮の実装はこの順。
- `TryPlace` は `CanPlace` と同じ答えを返し、`Ok` のときだけ状態を変える。**力と代金は仕組み側が引く**（画面は引かない）。所持金の変化は既存の `TomsModel.PlayerMoney` を通す（HUD はそれで動く）。
- `GetSlot` の `TurnsLeft` は「あと何日効くか」（1 以上）。`TotalTurns` は目盛りの数（入れたときの日数）。0 になったら null を返す。
- `Changed` は、入れたとき・朝の更新で残り日数が減った／外れたとき・配信で力が戻ったときに流す。画面は開いている間だけ描き直す。
- 品ごとのバイアスを読む口は今は無い（画面は使っていない）。要るなら足してよい。
- 品の一覧は画面が `ItemModel` から取る（仕組みは id で受けるだけ）。

## 仮の実装と差し替え方

- 仮の実装: `Assets/Scripts/Prophet/ProphetInterventionStub.cs`。メモリ上だけ（セーブしない・需要に効かない・お金を引かない。お金が足りないかは見る）。代金 15,000G・力の最大 2・効く日数 3。
  撮影・確認用に `AdvanceDay()` / `OnStreamFinished()` / `SetPower(n)` / `ForceSlot(dir, itemId, turnsLeft)` がある（本物には要らない）。
- DI の登録は 1 か所: `Assets/Scripts/GameLifetimeScope.cs` の
  `builder.Register<IProphetIntervention, ProphetInterventionStub>(Lifetime.Singleton);`
  → 本物を作ったら、この 1 行の `ProphetInterventionStub` を本物のクラスに替える（`Lifetime.Singleton`。コンストラクタ注入でよい）。
  仮の実装のファイルは本物が入ったら消してよい（撮影ツール `Assets/Scripts/Editor/ProphetCapture.cs` が使っているので、消すならそこも本物かテスト用の実装に替える）。

## UI が前提にしていること

- 力・枠・残り日数・代金はすべて `IProphetIntervention` から読む。画面は自分で数えない。
- 画面は開いたとき（`TomsShopGamePhase.Prophet` に入ったとき）と `Changed` のときに描き直す。`TryPlace` が `Ok` を返したら、`Changed` が来なくても描き直す。
- 開いたときの占い師の一言: 力が満タン → 「力があふれる前に使おう」、0 → 「次の配信まで休む」、それ以外は案内。0 のときは空の枠に「力が 0 のあいだは入れられない」の幕（押せるまま。押すと理由を言う）。
- 枠の上にカードを持ってきた（ドラッグ中・選んでからホバー／パッドで枠を選んだ）時点で `CanPlace` を呼び、Ok なら「需要 ▲ 3 ターン／力を 1 つ使う・15,000G・離すと入る」、だめなら理由を枠に出す。離す・押すで `TryPlace`。
- 品のカードの「新聞 ▲」は、プレイヤーが新聞のスクラップに書いた**予想メモ**（まだ答えの出ていないもの）に当てはまる品。記事から品へのリンクはゲームが作らない（`Docs/News_Spec.md` §9.3）ので、プレイヤー自身の予想だけを出す。
- 演出は省略モード（AutoPlay・マラソン中）なら出ない（`UIMotion`）。

## AI が押すための名前（変えない）

`ProphetView`（`Assets/Scripts/Prophet/ProphetView.cs`）。「品のカードを押す → 枠を押す」の 2 回で入る。

| 用途 | 名前 |
|---|---|
| 占い師の画面を開くボタン（店） | `TomsShopView` のフィールド `ProphetButton`（陳列フェーズに押せる。`TurnPhaseManager.GetPhaseForScreen`） |
| 開いているか | `ProphetView.IsOpen`（プロパティ） |
| 品のカードのボタン | `ProphetView.ItemCardButton(string itemId)`（公開メソッド。絞り込みで隠れていれば「すべて」に戻して探す。並んでいなければ null） |
| 上げる枠のボタン | `ProphetView.UpSlotButton`（プロパティ。フィールドは `upSlot`（ProphetSlotView）） |
| 下げる枠のボタン | `ProphetView.DownSlotButton`（プロパティ。フィールドは `downSlot`） |
| 向きで枠のボタン | `ProphetView.SlotButton(ProphetDirection direction)` |
| 閉じるボタン | フィールド `closeButton`（プロパティ `CloseButton`） |
| 選んでいる品 | `ProphetView.SelectedItemId`（無ければ null） |
| 選んでいる品をやめる | `ProphetView.ClearSelection()`（同じカードをもう一度押しても外れる。パッドは B / Esc） |
| 絞り込み | `ProphetView.SetFilter(ProphetItemFilter.All)` |

押した結果は `IProphetIntervention` の状態（`GetSlot` / `Power`）で確かめる。入らなかった理由は占い師のセリフ（`dialogueText`）にも出る。

## AI に足してほしい判断（案）

- 力が 2（満タン）なら使う（配信で戻る分をあふれさせない）。1 なら、配信が近く（あと 1〜2 日）にあるときに使う。
- 上げる枠: 新聞で上がると読んだ品のうち、在庫の値打ち（値段×在庫）が大きい品。代金の 2〜3 倍以上の在庫を持っているときだけ。
- 下げる枠: 仕入れたい品（次の配信の弱点の品・新聞で後から上がる品）を下げて安く買い、切れて戻ってから売る。
- 以前の試し（wt-05 で一度組んだ近い実装・バイアス 0.6＝新聞の「中」と同じ、3 日）では、品出し・新聞なしの相場で
  入れた品の値段がカードなしより 1 日目 ±15%・3 日目 +46% / −39%、切れた後も相場の勢いで 2〜3 日同じ向きに進んでから戻った（昇りの山は 6 日目 +60%、沈みは 12 日目に +22% まで振れ戻す）。需要の差は切れた翌朝から毎日 15〜20% ずつ縮んだ。
  ヘッドレスの戦略ボット（Medium・20 シード）では、15,000G だと在庫の値打ちが足りる日が少なく、1 ランに 1〜2 回しか使わなかった（純資産の差は誤差の範囲）。数値決めの参考に。

## 占い（ProphetFortune）を消すときに直す参照（grep、2026-10-30 時点）

画面からはもう呼んでいない（`ProphetPresenter` / `ProphetView` は参照しない）。残っているのは:

- `Assets/Scripts/Prophet/ProphetFortune.cs`（本体）
- `Assets/Scripts/Config/GameConstData.cs:311` コメント（`tavernProphetPrices` の説明。村の酒場の値段を残すなら文言を直す）
- `Assets/Scripts/Editor/AutoPlay/AutoPlayBatch.cs:341-344`（`-apSet fortune.*` の上書き）
- `Assets/Scripts/Editor/AutoPlay/AutoPlayCli.cs:162-163`（占い費の集計）
- `Assets/Scripts/Editor/AutoPlay/AutoPlayContracts.cs:209-210`（`AutoPlayItemView.FortuneDirection`）
- `Assets/Scripts/Editor/AutoPlay/AutoPlayHeadlessGame.cs:276-279, 695-698, 1002-1014`（`_fortuneTurn` / `FortuneSpend` / `BuyFortune`）
- `Assets/Scripts/Editor/AutoPlay/AutoPlayRunner.cs:72-73, 205-212, 498`（`TotalFortuneSpend` / `IAutoPlayFortuneBot` / `BuyFortune`）
- `Assets/Scripts/Editor/AutoPlay/AutoPlayStrategyBots.cs:315-347, 372-383, 419, 435-436`（`IAutoPlayFortuneBot` / `AutoPlayReaderBot` の fortune / ボット名 `reader_fortune`・`fortune`）
- 画面の古い部品（もう使っていない。消してよい）: `Assets/Scripts/Prophet/ProphetTrendRowUI.cs`、`Assets/Scripts/Prophet/ProphetRankingRowUI.cs`、`Assets/Prefabs/TrendRow.prefab`、`Assets/Prefabs/RankRow.prefab`（どれも占い師の画面からしか使われていなかった。`ProphetTrendRowUI.PlayDirection` は UI アニメの作業で足されたもの）
- 文言: `Assets/TomsScenario/Scenarios/CharacterIntro.csv` と `.book.asset` の `Intro_Prophet`（「この町の流行をボクのタロットで占ってあげよう」「今のトレンド武器…」）。宴のシナリオなので、直すなら CSV とブック両方
- 文書: `Docs/News_Spec.md` C5（「占い師が 3 日先を 15,000G で占う」＝新聞の有料の裏取り）・C6
- 実績・チュートリアル（`TutorialPresenter` ほか）は占いの機能を参照していない（「占い師」はキャラクターの呼び名としてだけ出る）
- 村の酒場の効果の説明文がスプレッドシートにあれば「占いの値段」の文言（シート側。GAS は触らない約束なので仕組み側で確認）

## 画面の配線（wt-05 が作ったもの）

| ファイル | 役目 |
|---|---|
| `Assets/Scripts/Prophet/ProphetView.cs` | 画面（力の玉・2 つの枠・品のカードの棚・絞り込み・並べ替え・ドラッグ／押す→枠／LB・RB） |
| `Assets/Scripts/Prophet/ProphetSlotView.cs` | 枠 1 つ（空き／確かめ／効いている＝鍵・目盛り／力 0 の幕・はまる演出） |
| `Assets/Scripts/Prophet/ProphetItemCard.cs` | 品のカード 1 枚（値段・在庫・新聞の向き・前日比・効果中） |
| `Assets/Scripts/Prophet/ProphetPresenter.cs` | `IProphetIntervention` を読んで描き、`TryPlace` の答えをセリフにする |
| `Assets/Resources_moved/ProphetDialogues.csv` | 占い師のセリフ（`power_full` / `power_empty` / `place_*` / `placed_*` ほか。古い `fortune_*` 行は消した） |
| `Assets/Scripts/Editor/ProphetUIBuilder.cs` | `FortuneTeller.prefab` を組み直す（`-executeMethod ProphetUIBuilder.BuildBatch`。冪等） |
| `Assets/Scripts/Editor/ProphetCapture.cs` | 撮影（`-executeMethod ProphetCapture.RunBatch -captureOut <dir>`。empty / up / both / power0 / preview） |

演出: 枠に入ると `CardFlip`（レア）でカードがめくれて `DragFeel.Puff`・枠が弾む。持ち上げは `DragFeel.Lift/Drop`、枠の光は `DragFeel.SetSlotGlow`、棚は `UIMotion.Stagger`、力を使うと玉が弾む。入れられないと枠が首を振る。

## 仕組みの実装（wt-04、2026-10-30）

仮の実装（`ProphetInterventionStub`）は消して、本物 `Assets/Scripts/Prophet/ProphetInterventionModel.cs` に替えた。
DI は `builder.Register<ProphetInterventionModel>(Lifetime.Singleton).As<IProphetIntervention>().AsSelf();`（画面は口、日送り・配信・需要の計算は本体の型を読む）。
撮影ツール（`ProphetCapture`）は保存しない形 `ProphetInterventionModel.CreateInMemory()` を使う（デバッグ用の `SetPower` / `ForceSlot` は本物にもある）。

| 項目 | 実装 |
|---|---|
| 数値の置き場 | `Assets/Scripts/Prophet/ProphetTuning.cs`（力 最大 2・開始 2・配信で +1、代金 15,000G、3 日、バイアス 0.6、適正値の傾き 0.7、直撃 0.03、AI のしきい値）。マスタ・GAS は触っていない |
| 効き方 | `ItemModel.ApplyShopTurnEconomy(..., prophet)` が朝の更新で読む。①需要の均衡点: ニュースと同じく Trend の外の別枠で ±0.6 を足す ②値段側の均衡点: 適正値 × (1 ± 0.7×0.6)（ニュースの 2 段目と同じ形。前日の適正値には前の朝の効き）③入れて最初の朝だけ需要へ ±0.03 の直撃 |
| 直撃の判断 | 入れたその日のうちは需要も値段も動かさない（陳列・売り注文の途中で値が変わらないように）。翌朝（最初に効く朝）にニュースの「小」と同じ 0.03 だけ直撃。控えめ |
| 日数 | 入れた日 TurnsLeft=3。朝の経済更新で効かせたあと `GameFlowManager.NextTurn` が `AdvanceDay()` → 3 回の朝に効き、3 回目の朝に外れる（枠に見えるのは入れた日を含め 3 日）。外れたらバイアスが消えるだけ（反動なし、需要はふだんの収束 15%/日 で戻る） |
| 力 | `BattleResultHandler.Start` が日送りの前に `OnStreamFinished()`（途中終了でも）。最大を超えない |
| 代金 | `VillageEffects.ApplyProphetPrice(15,000G)`（酒場 Lv4 以上 10,000G）。`TomsModel.PurchaseItem` → `SavePlayerMoney` |
| 保存 | ラン内のセーブ `prophetIntervention.json`（`RunSaveCleaner` の一覧に追加。旧 `prophetFortune.json` も新しいランで消す）。`{version, power, slots[{itemId, direction(+1/-1), turnsLeft, totalTurns}], expired[...]}`。ファイルが無い旧セーブ・power が欠けたファイルは「枠は空・力 2」。新しいランは `GameLifecycleHandler.InitializeNewGame` が `Clear()` |
| AI | 決まりは `Assets/Scripts/Prophet/ProphetAutoRule.cs` の 1 か所。力が満タンの日に 1 つだけ・代金を払って 3 万G 残るときだけ。上げる枠＝新聞▲で在庫の値打ちが代金の 2 倍以上の一番大きい品、下げる枠＝仕入れたい品でまだあまり持っていない（在庫の値打ちが代金未満）一番高い品 |
| ヘッドレス | `AutoPlayHeadlessGame` は同じ `ProphetInterventionModel` を本物の `GameFlowManager` / `BattleResultHandler` に渡す。ボットは `reader_prophet` / `prophet` |
| プレイ中の自動プレイ | `AutoPlayController.Prophet.cs`。陳列フェーズの頭に決まりへ聞き、上の名前で押す。失敗しても画面を閉じて店ホームへ戻す。人格（`PersonaStrategy`）は `IAutoPlayProphetUser` で人格のログに `prophet` を書く |
