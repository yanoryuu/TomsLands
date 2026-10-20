# オートプレイ（開発用・全シーン通し）使い方

人が操作しなくても、タイトル → 村 → 出店準備 → 店（仕入れ・陳列・営業・精算）→ 配信 → リザルト/ゲームオーバー を自動で進める開発用機能。
プレイ動画の自動撮影や、通しの動作確認に使う。

- コード: `Assets/Scripts/Debug/AutoPlay/`
  - `AutoPlay.cs` … 静的 API（開始・停止・状態取得・イベント）
  - `AutoPlayController.cs` … 常駐ドライバ（`AutoPlay.Start()` が `[AutoPlay]` という DontDestroyOnLoad の GameObject を作る）
  - `AutoPlaySettings.cs` … 設定（速度・ウェイト・会話の送り方・ラン終了時の動きなど）
  - `IAutoPlayStrategy.cs` … 判断の差し替え口（標準 / ランダム / 素通し）
  - `AutoPlayUi.cs` … ボタン押下・DI 解決・リフレクションのヘルパー
- エディタと Development Build だけでコンパイルされる（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`）。
- `AutoPlay.Start()` を呼ぶまで何も生成・購読しないので、**OFF のときの通常プレイには一切影響しない**。

## 起動方法

### デバッグメニューから
1. Play 中に F11でデバッグメニューを開く。
2. 「オート」タブの先頭「■ オートプレイ（全シーン通し・録画用）」のトグルを ON。
3. 速度（x1 / x2 / x4）、行動間ウェイト、戦略、会話の送り方、ラン終了時の動き、進めるターン数を選べる。
   - デバッグメニューはどのシーン（タイトル・村・準備・店・配信・リザルト・ゲームオーバー）にもあるので、どこからでも開始できる。
   - その下の「■ オートプレイ（実ゲーム画面を自動進行）」は従来からある店・配信だけの自動進行（DebugAutoPlayer）。
     全シーン通しのオートプレイは内部でこれをエンジンとして使う。

### エディタ拡張・スクリプトから
```csharp
// Play 中に呼ぶ（Play 中でなければ false）
AutoPlay.Start(new AutoPlaySettings
{
    TimeScale = 2f,
    ActionWait = 0.8f,
    ScenarioMode = AutoPlayScenarioMode.Read,
    TurnLimit = 10,
    RunEndAction = AutoPlayRunEndAction.Stop,
});
AutoPlay.Stop("録画終了");
```

## 静的 API（録画ウィンドウ連動用）

| API | 内容 |
|---|---|
| `bool AutoPlay.Start(AutoPlaySettings settings = null, IAutoPlayStrategy strategy = null)` | 開始（Play 中のみ）。実行中に呼ぶと設定・戦略だけ差し替える。settings はコピーして使う |
| `void AutoPlay.Stop(string reason = "手動で停止")` | 停止。timeScale・DebugAutoPlayer の設定を開始前に戻す |
| `void AutoPlay.Toggle()` / `Pause()` / `Resume()` | 切替・一時停止・再開 |
| `void AutoPlay.SetSpeed(float timeScale)` / `SetActionWait(float sec)` | 速度・ウェイトの即時変更 |
| `bool AutoPlay.IsRunning` / `IsPaused` | 実行中（一時停止中も true）/ 一時停止中 |
| `string AutoPlay.Status` / `LastStopReason` | いま何をしているか / 最後の停止理由 |
| `int AutoPlay.CurrentTurn` / `StartTurn` / `TurnsElapsed` | 現在ターン（`GameFlowManager.CurrentTurn`。店以外では最後に分かった値、不明なら -1）/ 開始時ターン / 経過ターン |
| `string AutoPlay.CurrentSceneName` | アクティブシーン名 |
| `string AutoPlay.CurrentPhaseLabel` | 例: `TomsShop/Shop/Procurement`, `TomsShop/BlackSmith`, `TomsShop/TurnEndSummary`, `FightScene/Streaming`, `VillageScene`。会話中は末尾に `+会話` |
| `GamePhase? CurrentGamePhase` / `TomsShopGamePhase? CurrentShopPhase` / `TurnPhase? CurrentTurnPhase` | 型付きのフェーズ（取れないシーンでは null） |
| `float AutoPlay.ElapsedRealtime` | 開始からの実時間（秒） |
| `IReadOnlyList<string> AutoPlay.LogLines` | 最近 50 行のログ |
| `event Action Started` / `event Action<string> Stopped` | 開始 / 停止（理由） |
| `event Action<int> TurnChanged` | ターンが変わった（開始後に最初に分かったときも 1 回出る） |
| `event Action<string> PhaseChanged` | `CurrentPhaseLabel` が変わった |
| `event Action<string> Logged` | ログ 1 行（時刻付き） |
| `AutoPlaySettings AutoPlay.Settings` / `IAutoPlayStrategy AutoPlay.Strategy` / `AvailableStrategies` | 設定・戦略・戦略一覧 |

- イベントの購読者は Play をまたいでも保持される（エディタ側から一度購読すればよい）。状態は Play 開始ごとに初期化される。
- Play 終了などでコントローラーが破棄されると `Stopped("コントローラーが破棄された（Play 終了など）")` が出る。
- 停止理由の例: `"N ターン進めたので停止（T1→T11）"`, `"ラン終了（リザルト表示まで完了）"`, `"ラン終了（ゲームオーバー）"`,
  `"タイトル画面に到達"`, `"詰まりから復帰できないため停止（…）"`, `"デバッグメニューから停止"`。

## 設定（AutoPlaySettings）

| 項目 | 既定 | 内容 |
|---|---|---|
| `TimeScale` | 1 | 速度倍率（`Time.timeScale`）。実行中は毎フレームこの値に保つ。停止で開始前の値に戻す |
| `ActionWait` | 0.8 秒 | 行動と行動の間のウェイト（店・配信エンジンのステップ間隔にも使う） |
| `WaitFollowsTimeScale` | true | ウェイトを timeScale に連動（x2 なら実時間は半分） |
| `ButtonHoldSeconds` | 0.12 秒 | ボタンの押し込み表示時間（実時間） |
| `ScenarioMode` | Read | 会話: `Read`=一定間隔で送る / `Fast`=毎フレーム送る / `Off`=送らない（手で送る） |
| `ScenarioReadInterval` | 1.2 秒 | Read での 1 メッセージの間隔 |
| `TurnLimit` | 0 | 進めるターン数（0=無制限） |
| `TitleAction` | ContinueOrNewGame | タイトル: セーブがあれば続きから、無ければ空きスロットで初めから（**上書きはしない**） |
| `PreferredSlot` | -1 | 使うスロット（-1=自動） |
| `RunEndAction` | Stop | ラン終了時: `Stop`=リザルト/ゲームオーバーの演出・会話を流してから停止 / `StartNextRun`=村→準備→次のランへ |
| `SceneSettleSeconds` | 1.5 秒 | シーンに入ってから最初の行動までの待ち |
| `StallSeconds` | 40 秒 | 状態がこの時間変わらなければ「詰まり」 |
| `MaxStallRecoveries` | 5 | 詰まり復帰の連続試行上限（超えたら停止） |
| `PauseRecoverySeconds` | 4 秒 | 店・配信エンジンが一時停止してから自動復帰を試すまで |
| `PauseOnErrorLog` | false | LogError/例外でエンジンを一時停止するか（true でも自動復帰の対象） |
| `AutoAnswerUnknownPopups` | true | 自動で選べない確認ダイアログに「キャンセル（無ければ確認）」で答える |
| `StreamActionsEnabled` | true | 配信中に介入・補充・棚の入れ替え・値動き確認を行う（false なら自動進行を待つだけ） |
| `StreamActionInterval` | 3 秒 | 配信中の操作の間隔（WaitFollowsTimeScale に従う。ActionWait より短くならない） |
| `StreamSpendRatio` | 0.5 | 1配信で介入に使ってよい額（配信開始時の残高に対する割合） |
| `PriceGraphPeekSeconds` | 1.5 秒 | 値動きグラフを開いてから閉じるまで |
| `StreamPrepTurns` | 1 | 次の配信まで何営業日以内なら「配信前の仕入れ」（ダンジョン向け）にするか（1 = 配信の前日と配信日の朝） |
| `IntelPrepTurns` | 2 | 次の配信まで何営業日以内なら、弱点が分からないとき情報屋で情報を買うか（戦略が判断） |
| `RandomSeed` | 0 | 戦略の乱数シード（0=時刻） |

## 各シーン・フェーズでの自動行動

| 場面 | 行動 | 手段 |
|---|---|---|
| 会話（Utage・チュートリアル含む） | Read/Fast で送る。選択肢は戦略が選ぶ（標準は先頭）。会話中は他の操作をしない | `AdvEngine.Page.InputSendMessage()` / `SelectionManager.SelectWithTotalIndex()` |
| タイトル | スタート → 続きから/初めから → スロット選択 | ボタン押下（押し込み表示＋`onClick.Invoke`） |
| 村 | 帰還報告・投資パネルを閉じる → 出店準備へ（演出中は 3 秒ごとに押し直す） | ボタン押下 |
| 出店準備 | （戦略）村資金を上限まで持ち込む → スターターレリック選択 → 出店。会話・ロック中は待つ | ボタン押下 |
| 店: イベント | ページ送り・確定 → 次へ | DebugAutoPlayer（`confirmButton.onClick.Invoke` / `TurnPhaseManager.AdvanceTurnPhase`） |
| 店: 仕入れ | 鍛冶屋を開く →「オート購入」→ 方針ドロップダウン・予算入力 →「購入」→ 閉じる。配信前は下の「配信前の仕入れ」。初回チュートリアルのロック中も同じ経路 | AutoPlayController.Procurement（実ボタン押下）。開く・閉じるは DebugAutoPlayer |
| 店: 配信前の情報収集 | 次の配信先の弱点が不明なら 情報屋 → 地図 → ダンジョン →「情報を買う」→ 閉じる | AutoPlayController.Procurement（実ボタン押下） |
| 店: 陳列 | 陳列パネルを開く → オート陳列 → 閉じる | DebugAutoPlayer（`ItemSelectionPresenter` + `ItemSelectionView.OnAutoDisplayRequested`） |
| 店: 営業 | 営業開始 → 演出待ち | DebugAutoPlayer（`TomsShopView.OnStartShopClicked`） |
| 店: 精算（サマリー） | 確認 → 次のターンへ | DebugAutoPlayer（`TurnEndSummaryView.OnConfirmClicked`） |
| 店: 割り込み | 納税パネル（払える→支払う / 任意→閉じる）、レリック3択（戦略）、朝レポートを閉じる、各種お知らせを閉じる | DebugAutoPlayer |
| 店: 破産 | 「結果へ」を押してゲームオーバーへ | ボタン押下 |
| 配信日の朝 | 鍛冶屋へ寄る（戦略）→ 配信を始める | DebugAutoPlayer（ポップアップの OnConfirm/OnCancel） |
| 配信（FightScene）準備・リザルト | 品出しオート選択→確定 / リザルト確認 | DebugAutoPlayer |
| 配信中 | 介入・補充・棚の入れ替え・値動き確認（下の「配信中の操作」） | AutoPlayController.Streaming（実ボタン押下・ドラッグ再現） |
| リザルト | 演出・会話を流す → `Stop` なら停止 / `StartNextRun` なら村へ | ボタン押下（精算 → セーブ削除 → 村 の順序は ResultPresenter が守る） |
| ゲームオーバー | 会話を流す → `Stop` なら停止 / `StartNextRun` なら村へ | ボタン押下（同上、GameOverPresenter） |
| 店の別画面（新聞・地図など） | エンジンが一時停止 → 汎用「閉じる」→ 無ければ `StateManager.ChangeTomsShopPhase(Shop)` | 自動復帰 |
| 確認ダイアログ（未知） | キャンセル（無ければ確認）。タイトルの削除/上書き確認は必ず「戻る」 | `PopUpData.OnCancel/OnConfirm` |

### 詰まり検知
- `シーン | フェーズ | ターン | エンジン状態` が `StallSeconds` 変わらなければ Warning ログを出し、画面上の押せるボタンから
  「閉じる / 次へ / OK / 戻る」らしいもの（名前・ラベルで判定、最前面のキャンバス優先）を 1 つ押す。
  タイトル・削除・上書き・購入・売却・投資・支払いなどを含むボタンは押さない。`MaxStallRecoveries` 回を超えたら停止。
- 店・配信エンジン（DebugAutoPlayer）自身も 30 秒進まないと一時停止する。これも `PauseRecoverySeconds` 後に自動復帰を試す。
- 会話中・配信中（自動進行待ち）は詰まり判定しない。

## 配信中の操作

配信パネル表示中、`StreamActionInterval` ごとに 1 つ操作する（判断は戦略）。お金は既存の `InterventionPresenter` / `BattleSceneStarter` が
「利用可能残高（所持金＋配信中売上−補充費−介入支出）」で扱い、戦闘終了時に一括精算するので、AutoPlay はボタンを押すだけで精算・配信終了処理には触れない。

| 操作（プレイヤーができること） | 自動化 | 手段 / 判断（標準戦略） |
|---|---|---|
| 在庫切れの補充ポップアップ（数量選択→購入 / やめる） | する（最優先。出ている間は戦闘が止まる） | 少し見せてから数量スライダー→「購入」ボタン押下 / 「やめる」。標準は既定数（10個・買える範囲）。売れ残りは返金される仕様 |
| 在庫 → 売り場のドラッグ入れ替え（売り場内・在庫内の入れ替えも可能） | する（在庫→売り場のみ） | beginDrag→drag→drop→endDrag を ExecuteEvents で再現（ゴーストアイコンも出る）。標準は売り場の空き・売り切れを、在庫×価格が最大の在庫で埋める |
| 介入カード 勇者側: 回復（緑）/ スキル（青）/ 必殺（赤） | する | 実ボタン押下。標準: 勇者HP35%未満で回復、ボスがいて余裕があればスキル |
| 介入カード ダンジョン側: 罠 / 呪い / 増援 / ボス強化 | する | タブボタンでカードをめくってから押下、あとで表へ戻す。標準: 勇者HP75%超のとき 35% の確率で罠（配信を延ばして販売機会↑） |
| 敵タップ（青・赤スパの対象指定） | する（スキル・必殺の直前） | `CharacterView.OnClicked`（敵スプライトのクリックと同じ通知）。標準はボス優先、次に一番弱っている敵 |
| 売り場の商品クリック → 値動きグラフ | する（見た目用） | スロットにポインタイベント→少し後にグラフの閉じるボタン。標準 15% / ランダム 30% |
| 介入カードのスワイプでめくる | しない（タブボタンで同じことをする） | - |
| 一時停止 / 配信終了ボタン | しない | 配信の結果・精算を変えるため介入しない |
| ステルスマーケティング（StreamingView） | しない | シーン・プレハブに配置されておらず実際には使えない（旧UI） |

- 使ってよい額: 配信開始時の残高 × `StreamSpendRatio` − この配信で使った額。標準戦略は配信日の朝の仕入れで `StreamReserveGold`（40,000G、使える額の半分まで）を残す。
- ランダム戦略: 介入は使えるものから等確率（40% は何もしない）、補充は 70% で 1〜上限個、狙う敵もランダム。素通し戦略: 配信中は何もしない。
- Utage のバックログ（マウスホイール）・メッセージ非表示（右クリック）が開いて会話が止まった場合は通常表示へ戻す。

## 配信前の仕入れ（ダンジョンに合わせた武器）

### 相性の仕組み（ゲーム側）
- ダンジョン（`DungeonInfoScriptableObj` / `DungeonData`）の `requiredAttribute` が「弱点属性」。出てくる魔物の属性（`EnemyData.ElementType`）に有利な属性になっている
  （例: 深緑の獣林=木の魔物 → 弱点 火、氷霧の洞窟=水の魔物 → 弱点 木。有利関係は `ElementAttributeMapper.IsEffective`: 水→火、火→木、木→水、光⇔闇）。
- 武具の属性は `RuntimeItemData.ItemAttribute`。配信中に魔物を倒すたび、その魔物に**有利な属性の商品は価格×`effectiveAttributeRate`・需要＋**、不利な属性は価格・需要−
  （`StreamingSalesController` 案5 / `BattleDemandTracker` 案A）。品出しのオート選択（`StreamingSettingPresenter`）も弱点属性・有利属性を加点する。
- **勇者のクリア率には属性は効かない**（装備ボーナス `HeroEquipmentBonus` はランク＝requiredLevel のみ。そもそも勇者の装備は店の在庫と別）。相性が効くのは配信中の売上（価格・需要）。
- 弱点は「知っている」ときだけ使える: 情報屋で情報を買う（`InfoBrokerPresenter.PurchaseDungeonInfo` → `DungeonIntelModel.MarkWeaknessKnown`）と、
  鍛冶屋の上部バナーに「弱点:X」が出て、お任せ購入の「次ダンジョン特化」（`AutoBuyStrategy.DungeonFocus`）が弱点属性を優先するようになる。知らなければバナーは「弱点:?」で特化も効かない。
- 出現する魔物の属性は `DungeonData.dungeonMonsters`（現在のダンジョンLvの `EnemyData.elementType`）。ゲーム側の品出しオート選択と同じく、**ダンジョン情報を買うまでは使わない**。
  属性ごとに「有利な魔物の数 − 不利な魔物の数」（`IsEffective` / `IsWeak`）が正なら有利、負なら不利。現在のデータでは
  深緑の獣林（木）: 有利=火 / 不利=水、灼熱の火山牢（火）: 有利=水 / 不利=木、氷霧の洞窟（水）: 有利=木 / 不利=火、
  魔王城・忘却の霊廟（闇）: 有利=光、古代機構城メタリオン（光）: 有利=闇。アイテム属性は敵と同じ5属性（火・水・木・光・闇）で、旧「風」は木、旧「土」は闇に統一済み（#83）。
- 次の配信先は `GameFlowManager.GetNextBattleDungeon()`（配信日の朝の寄り道中は `PendingBattleDungeon`）、残り営業日は `GetTurnsUntilNextBattle()`（次の行き先が配信なら 0）。画面の表示は別の `GetDaysUntilNextStream()`（今日 0・明日 1）を使うので、AutoPlay の判断とは数え方が1つずれる。
  情報料は `InfoBrokerModel.GetDungeonInfoCosts()[ダンジョン][配信回数]`。魔王城など情報屋で扱っていないダンジョンは弱点を知る手段がない。

### 自動の流れ
1. 次の配信まで `IntelPrepTurns` 以内の仕入れフェーズで、弱点が不明なら戦略の `ShouldBuyDungeonIntel` を聞き、買うなら
   店の「情報屋」→「地図」→ 配信先 →「情報を買う」→「もどる」を押す（標準: 情報料が所持金の 25% 以内なら買う）。
2. 次の配信まで `StreamPrepTurns` 以内（配信の前日・配信日の朝の寄り道）の鍛冶屋で、戦略の `ChooseStreamPurchase` に従って
   - 有利属性の**武器**を商品一覧から個別に買う（武器タブ → 行を選ぶ → 数量スライダー →「仕入れる」）。
     標準: 弱点属性（requiredAttribute）の武器を最大 2 種 → ほかの有利属性の武器を最大 2 種（足りない側はもう一方で埋めて計 4 種まで）、
     それぞれ期待収益の高い順。不利属性は候補にしない。ダンジョン向け予算を種類数で均等割り
   - ダンジョン向け予算の残りを「オート購入」の方針「次ダンジョン特化」で買う（弱点属性の防具など。特化対象が尽きたぶんはおすすめ順で埋まる＝ゲーム仕様）
   - 予算の残りをいつもの方針（おすすめ順）で買う
   - 標準のダンジョン向け予算 = 仕入れ予算 × `StreamFocusShare`（0.6）。ランダムは 30〜90%。素通しは何もしない
3. 理由と結果をログに出す（例:「配信前の仕入れ: 氷霧の洞窟（水）: 有利=木 / 不利=火 → 有利属性の武器を優先: 若木の弓(木)×75・大樹の槍(木)×73・木の葉の剣(木)×77」
  「仕入れ結果（氷霧の洞窟 弱点:木）: 木属性 225/297個（うち武器 225）・有利属性(木) 225個（うち武器 225）」）。
- 仕入れ予算そのもの（`ChooseProcurementBudget`）は従来どおりで、配信日の朝は介入用の `StreamReserveGold` を残す（両立）。

## 戦略の差し替え

```csharp
public class MyStrategy : DefaultAutoPlayStrategy
{
    public override string Name => "半額仕入れ";
    public override int ChooseProcurementBudget(AutoPlayContext ctx) => ctx.Spendable / 2;
    public override AutoBuyStrategy ChooseAutoBuyStrategy(AutoPlayContext ctx) => AutoBuyStrategy.Bargain;
}

AutoPlay.Strategy = new MyStrategy();          // すぐ反映
AutoPlay.AvailableStrategies.Add(new MyStrategy()); // デバッグメニューのボタンに出す
```

`IAutoPlayStrategy` の判断: 仕入れるか / 予算 / 仕入れ方針 / 陳列するか / 配信日の朝に鍛冶屋へ寄るか / レリック3択 / 会話の選択肢 /
配信中: `ChooseStreamIntervention` / `ChooseEnemyTarget` / `ChooseRestockQuantity` / `ChooseShelfSwap` / `ChoosePriceGraphPeek`（`AutoPlayStreamContext`: 残高・使ってよい残り額・勇者HP・敵・介入ボタン状態・売り場/在庫）。
配信前: `ShouldBuyDungeonIntel` / `ChooseStreamPurchase`（`AutoPlayStreamPrepContext`: 配信先・残り営業日・弱点を知っているか・弱点属性・魔物の属性 `EnemyElements`・有利属性 `EffectiveAttributes`・不利属性 `WeakAttributes`・情報料・所持金・予算・有利属性で買える武具 `Matching`）。
準備で村資金を持ち込むか / スターターレリック。`AutoPlayContext` には ターン・所持金・2 ターン以内の納税額（`TaxReserve`）・
鍛冶屋Lv・店Lv・配信日の朝か・乱数 が入る。

- 標準: お任せ仕入れ（おすすめ順・納税準備金を残して全額）＋オート陳列、選択肢は先頭。
- ランダム: 予算 50〜100%、仕入れ方針・レリック・会話の選択肢・スターターレリックをランダム。
- 素通し: 何も買わず並べず進める（進行・演出の確認用。すぐ破産しやすい）。

## 既知の制約・注意
- 店・配信の操作は既存の DebugAutoPlayer を使うため、多くは View の Subject 発火（ボタン押下と同じ処理経路）で、
  ボタンの押し込み表示は出ない。押し込み表示が出るのはタイトル・村・準備・リザルト・ゲームオーバー・破産・汎用復帰と配信中（介入・補充・グラフ）のボタン。
- 朝レポートはパネルを非表示にするだけ（DebugAutoPlayer の既存挙動）。
- 村の施設投資・準備のスタートダッシュ（宣伝ビラ等）・難易度変更は行わない（難易度はタイトルの既定値「ふつう」）。
- 新聞・地図などの任意画面は開かない（情報屋は配信前に弱点を調べるときだけ開く）。
- 「初めから」は空きスロットのみ（上書き確認には必ず「戻る」で答える）。空きが無ければ停止する。
- 配信中の戦闘・コメント・視聴者スパチャはゲームの自動進行に任せる（プレイヤー側の操作だけを行う）。
- `TimeScale` を上げると、演出や配信の自動進行が速くなるぶん Read モードの会話も速く送られる（`WaitFollowsTimeScale`）。
- デバッグメニュー下段の従来オート（DebugAutoPlayer）を手で「停止」すると、全シーン通しのオートプレイも停止する。
- 実機での通し確認は未実施（コードはシグネチャ確認のみ）。初回は x1・ターン数 1〜5 で挙動を見ること。
