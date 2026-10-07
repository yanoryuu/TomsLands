# Jev AutoPlay 設計書（自動テストプレイ）

最終更新: 2026-10-05 / ブランチ: feature/news / 実装: `Assets/Scripts/Editor/AutoPlay/`

## 0. 目的

Jev（TypeSafe System One）を店主の代わりにして、ランを最初から最後まで自動で回す。
各ターンの判断（仕入れ・陳列・魔王軍支援・設備投資・配信の品出し・レリック3択）を Jev に下させ、
**所持金推移・破産・勇者の勝敗とレベル・売上内訳・判断の確率分布・異常**をレポートに残す。
用途は **バランス調整** と **バグ発見**。

- **開発時専用**。ゲーム本体（ランタイム）からは絶対に呼ばない。コードは全部 `Assets/Scripts/Editor/` 配下（ビルドに含まれない）。
- API キーは環境変数 `TYPESAFE_API_KEY` のみ。ファイル・ログ・レポートには書かない。
- API キーが無くても、**貪欲ボット / ランダムボット** で同じ仕組みが回る（比較の基準）。

---

## 1. 実行方式の判断

| 方式 | 中身 | 長所 | 短所 |
|---|---|---|---|
| (a) Play モード | 実シーンの DI コンテナから Presenter/Model を取り出して操作 | UI・演出・シーン遷移まで本物 | 遅い（演出・リアルタイム戦闘）。FightScene は `StreamingSettingPresenter.RunAsync`（確定ボタン待ち）・補充ポップアップ・リザルト画面のクリック待ちがあり、Battle/ に操作口を足す必要がある |
| (b) ヘッドレス | モデル層を直接組み立て、UI なしでターンを回す | 速い（1ラン数秒）・大量試行・シード再現 | 配信（戦闘＋配信販売）はサロゲート。Presenter にある処理は写しが要る |

**MVP は (b) ヘッドレスを採用。** 理由:

1. **ターン進行の本体はモデル側にある。** 日送り（`GameFlowManager.NextTurn`：経済更新・ニュース・売り注文の持ち越し精算・配当・バズ）と、
   配信結果の反映（`BattleResultHandler.Start`：在庫減算・入金・経験値・D1波及・レリック報酬・日送り）は
   **どちらも View を持たない普通のクラス**なので、本物をそのまま `new` して使える。
2. **Presenter に寄っている処理は少なく短い。** 写しが要るのは
   営業サマリーの売り注文化（`TurnEndSummaryPresenter.Entry` の約20行）・借金の強制返済（`DebtPresenter.ShowForced/OnPay`）・
   購入（`BlackSmithPresenter.HandlePurchase`）・魔王軍支援（`DungeonLevelUpPresenter.HandleLevelUp`）・陳列枠ガード の5か所だけ。
   どれもコメントで「〜の写し」と明記してある（本体が変わったら追従が要る＝§9 未決）。
3. **配信だけは Play モードでも重い。** FightScene はリアルタイム戦闘＋UI 待ちで、しかも Battle/・Streaming/ は
   別エージェントが作業中（ニコニコ式コメント・ウェーブ数）。ここに操作口を足すのは今はやらない。
4. **配信日に止まる口が既にある。** `GameFlowManager.SetPreStreamHandler`（配信前の寄り道）を登録すると、
   日送りが配信ノードに入った時点で FightScene へ行かずに制御が返ってくる。ヘッドレスはこれを使う。

(a) は Phase 3 で同じ `IPlayerActions` を実装する形で足す（§8）。サロゲートの較正にも使う。

### 1.1 何が本物で何が写しか

| 処理 | ヘッドレスでの扱い |
|---|---|
| 新規ラン初期化 | `GameLifecycleHandler.InitializeNewGame` の写し（各モデルの初期化メソッドは本物） |
| 日送り・経済・ニュース・バズ・配当・持ち越し精算 | **本物**（`GameFlowManager.NextTurn`） |
| フロー生成（自動生成・シード） | **本物**（`GameFlowManager.InitializeFlow`） |
| 仕入れ | 写し（`HandlePurchase`）／おまかせは**本物**（`ItemModel.AutoPurchase`） |
| 陳列 | 写し（枠ガード） |
| 営業→売り注文→翌日約定 | 写し（`TurnEndSummaryPresenter.Entry` の評価・View 部分を除く）＋**本物**（`SellOrderModel`・`ItemModel.SimulateShopSales`） |
| 借金の強制返済・破産判定 | 写し（`DebtPresenter`）＋**本物**（`DebtCalculator`・金融資産の強制売却） |
| イベント（インライン） | **本物**（`TomsEventExecutor.Execute`。選択肢が無いので自動で確認） |
| 魔王軍支援 | 写し（`HandleLevelUp`） |
| レリック3択 | **本物**（`RelicRewardService`） |
| 配信の勝敗 | **サロゲート**（§2.4） |
| 配信販売 | **サロゲート**（§2.4） |
| 配信結果の反映・日送り | **本物**（`BattleResultHandler.Start`） |
| 朝刊を開く・UI 演出・トコの会話 | 無し |

### 1.2 隔離

- セーブ: `SaveSlotManager.DevRootOverride` で `Temp/AutoPlay/<batch>/<run>/` へ逃がす（終了時に削除）。**ユーザーのスロットは触らない。**
- シーン遷移: `SceneTransitionService.DevSceneLoadInterceptor` で横取りし「FightScene / ResultScene / GameOver へ行こうとした」ことだけ記録。
- `BattleInputData` などの共有 ScriptableObject はプロジェクトのアセットを使わず `CreateInstance` した使い捨て。
- `DungeonRepository`（MonoBehaviour）は非アクティブ GameObject に付けて Awake を走らせない（Awake は現行スロットのセーブを読むため）。

---

## 2. 構成

```
AutoPlayWindow（EditorWindow: Tools/TomsLands/Jev AutoPlay）
  └ AutoPlayBatch（ボット×シードのランを交互実行・フック設置・レポート出力）
      └ AutoPlayRunner（1ラン：ステージに応じてボットに聞く → IPlayerActions で実行 → 記録・異常検知）
          ├ IAutoPlayBot ……… AutoPlayJevBot / AutoPlayGreedyBot / AutoPlayRandomBot
          └ IPlayerActions … AutoPlayHeadlessGame（本物のモデル群）
                               └ AutoPlayBattleSurrogate（配信の勝敗・配信販売）
AutoPlayReport（runs / turns / decisions / anomalies .csv ＋ summary.json ＋ summary.md）
```

### 2.1 プレイヤー操作 API（`IPlayerActions`）

UI のボタン操作と1対1の関数。ボットもランナーもこれしか触らない（将来 Play モード版も同じ契約で実装する）。

| 操作 | UI での相当 |
|---|---|
| `Snapshot()` | 画面に出ている情報（未来の Trend・未購入の弱点は載せない） |
| `Buy(item, qty)` / `AutoBuy(budget, strategy)` | 鍛冶屋の購入 / おまかせ仕入れ |
| `SupportDungeon(d)` | 魔王軍支援（ダンジョンのレベルアップ） |
| `UpgradeBlacksmith()` / `UpgradeShop()` | 鍛冶屋・店のレベルアップ |
| `ClearDisplay()` / `SetDisplay(item, qty)` | 陳列設定 |
| `ConfirmPendingEvent()` / `ChooseRelic(i)` / `DeclineRelic()` | ポップアップ |
| `StartSales()` → `EndDay()` | 営業開始 → サマリーの確認（日送り） |
| `StartStream(items)` | 配信の品出し確定 → 配信 |

不正な手（資金不足・枠超過・ロック中の銘柄）は例外にせず `Ok=false` で返し、**却下手**として数える（ボットのルール理解度・UI ガードの検証になる）。

### 2.2 1日の流れ（ランナー）

```
営業日: BeginShopDay（強制返済→破産判定、保留イベント確認）
       → レリック3択があればボットに聞く
       → ボットに営業日の計画を聞く（1リクエスト）
       → 設備投資 → 魔王軍支援 → 仕入れ → 陳列 → StartSales → 記録・不変条件チェック → EndDay
配信日: ボットに品出しを聞く（1リクエスト）→ StartStream（サロゲート→本物の BattleResultHandler）→ 記録
終了:   ResultScene へ遷移しようとした＝完走 / GameOver＝破産 / 400ステップ超＝停滞 / 例外＝クラッシュ
```

### 2.3 並列（交互実行）

UnityEngine.Random・AssetDatabase・モデルのファイル I/O はメインスレッド専用なので、**スレッド並列にはしない**。
代わりに複数ランを async で同時に走らせ、Jev の応答待ち（数百 ms）だけを重ねる。

- モデルに触る区間は必ず `AutoPlayRunContext.Enter()` の中で**同期的に**行い、await を跨がない。
- `Enter()` の間だけ静的状態をそのランへ切り替える: セーブ先・`UnityEngine.Random.state`（ラン毎に保存/復元＝**交互実行でもシード再現**）・ログの帰属先・シーン遷移の受け手。
  `RelicBattleEffects` は配信直前に取り直す。
- 同時進行数は `MaxConcurrent`（既定 8）。ベースラインボットは同期で終わるので、1ターン毎に `Task.Yield` してエディタを固めない。
- **メインスレッドから Jev を同期待ちしない**（デッドロックの罠: project_jev_market_sim）。`JevApi.SendAsync` は ConfigureAwait(false) 済み、
  ボット側は await の継続をメインスレッドに戻す。バッチの入口は `async void`。

### 2.4 配信サロゲートとその限界

| 項目 | 実機 | サロゲート |
|---|---|---|
| 勝敗 | `BattleFlowManager` の決定論戦闘 | `ClearProbabilityCalculator` と同じルールのドライラン（＋`HeroPowerMul` を `CharacterModel` と同じ丸めで適用）。オプションで「表示確率で抽選」 |
| 撃破数（経験値） | 実際の撃破 | ドライランの撃破数 |
| 配信販売 | 時間ループ＋戦闘ターン毎の販売、`BattleDemandTracker`、配信熱による価格変動、属性相性の値動き | `StreamingSalesModel.ProcessSingleItemSale` と同じ式（需要×SalesRate×max(1,陳列数)、端数は確率）を **戦闘1ターンごとに `StreamSalesScale` 回**（戦闘中の売上は介入の利用可能残高にも入る）。価格は配信開始時のまま |
| 介入（スパチャ） | `InterventionPresenter` / `InterventionCommandQueue` / 各 Command | 写し（§2.5） |
| 視聴者スパチャ | `SuperChatGenerator`（同接・熱で発生率が変わる） | 赤スパ起点の必殺技だけを「1戦闘ターンあたり確率 p」で再現（収入には入れない） |
| 補充ポップアップ | あり | なし |
| 防衛報酬 | `rewardGold × DefeatRewardMul` | 同じ |

→ **配信の売上の絶対値は信用しない。** 比較（ボット間・設定間）と、店側の経済・借金・成長曲線の検証に使う。
Phase 3 で Play モードの実配信を数十回記録し、`StreamSalesScale` を較正する。

表示確率とドライランが食い違ったら `clearprob_mismatch` を出す（例: レリックの HeroPowerMul は**表示確率に反映されていない**ので、
弱体化レリック所持時に「表示 70% なのに負ける」が起きうる＝実バグ候補）。
整合チェックは「介入なし」のドライランで行う（介入で勝敗が変わるのは仕様なので異常にしない）。

### 2.5 配信中の介入（スパチャ）のモデル化（2026-10-05 追加）

仕様は Docs/Streaming_Redesign.md §4・§5。本体（`Battle/Intervention/`）は読むだけで変更せず、AutoPlay 側に写しを持つ。
数値（金額・倍率・上限・クールダウン）はすべて `StreamingInteractionSettings`（Addressable → RemoteBalance → 既定値。`StreamingInteractionSettings.Load()`）から読む。

**写した処理**（`AutoPlayBattleSurrogate.BattleRun`）

| 本体 | 写し |
|---|---|
| `InterventionCommandQueue`（指示は同時に1件、勇者側は勇者の手番の頭・ダンジョン側は魔物の手番の頭で実行、視聴者の必殺技は別枠1件で勇者側の指示が無いときに実行） | 同じ順序 |
| `InterventionCommandQueue.ResolveAttack`（勇者の攻撃: 強化倍率→呪い×→ボス強化中ボスへは被ダメ×。魔物の攻撃: ボス強化中ボスは攻撃×、罠は最大HP×割合の防御無視ダメージを1回） | 同じ式・同じ消費順 |
| `CharacterPresenter.PerformAttack` / `CharacterModel.ApplyDamage`（max(1, 攻撃 − 防御)）/ `ApplyBonusDamage`（倒れていたら入らない）/ `Heal`（最大HPまで） | 同じ式 |
| `ReinforceCommand`（現在フェーズの通常魔物から抽選→全フェーズの通常魔物。ボスは出さない） | 同じ（抽選はランの System.Random） |
| `InterventionPresenter.Stop`（決着で未実行の指示は返金） | 同じ |
| 必殺技の上限（プレイヤー＋視聴者、予約ベースで数える）・ボス強化の上限 | 同じ |
| 精算 `純利益 = 売上 − 補充 + 返金 + 防衛報酬 − 介入支出 + 未実行の返金`、利用可能残高 `所持金 + 戦闘中売上 − 補充 − 介入支出` | 同じ（補充は未再現なので 0） |

**時間の近似（離散モデル）**: 戦闘は一括で解くので、ボットは配信前に「何を・配信のどのあたり（timing 0〜1）で使うか」の**予定リスト**を渡す。

- timing → 戦闘ターン: 介入なしで何ターンかかるか（H）を先にドライランし、`round(timing × (H−1))` ターン目の頭で出す。
- その時点で指示が実行待ち、またはクールダウン中なら、出せるターンまで**待つ**。上限超過・残高不足ならその予定は**捨てて**次の予定を見る（理由は streams.csv の skipped）。
- クールダウン（秒）は `ceil(cooldownSeconds / SecondsPerBattleTurn)` ターン（既定 6秒 ÷ 3秒 = 2ターン）。`SecondsPerBattleTurn` は較正値（設定で変更可）。
- 視聴者の赤スパ: SO の `viewerSuperChatEnabled && viewerRedTriggersSpecial` が ON のとき（設定で強制 ON/OFF も可）、1戦闘ターンあたり確率 `ViewerRedChancePerTurn`（既定 0.03・較正値）で必殺技を予約。同接・熱による発生率の変化は再現しない。
- 抽選モード（勝敗を表示確率で抽選）では、介入で勝敗がドライランから変わったときだけドライランの結果を採用し、それ以外は従来どおり抽選する。

**判断の手**（`IPlayerActions.StartStream(items, interventions)`）: 配信日のスナップショットに `Stream`（ダンジョン・表示クリア確率・ボス有無・魔物数・防衛報酬の見込み・装備込みの勇者の HP/攻撃/防御・介入メニュー（価格・払えるか・英語の効果説明））を載せる。
メニューには貪欲ボット用の**オラクル**（その介入を中盤に1回使ったら勝敗とターン数がどうなるか、介入なしの勝敗）も入るが、**Jev の state には載せない**（プレイヤーは見られない）。

| ボット | 介入の手 |
|---|---|
| 貪欲 | 期待値ベース（オラクル使用＝上限側の基準）: `EV = 防衛報酬の増減（勝敗が反転するか） + 配信が延び/縮むぶんの売上 − 価格` が正で最大の1件を中盤に |
| ランダム | 30% で1件（うち 1/3 は2件）、種類・タイミングとも一様 |
| Jev | §3.2 の `stream_intervene` / `intervene_timing` |

**対照群**: `ControlRunsWithoutInterventions`（既定 ON）で、同じシードを介入なしで回すボット `greedy+noint` / `random+noint` を自動で足す（Jev を含めるかは `ControlRunsIncludeJev`、費用2倍）。
各配信は「介入なしならどうだったか」もドライランで記録するので、対照群が無くても配信単位の反転は測れる。

---

## 3. Jev に聞く質問

### 3.1 state（英語・画面で見える情報だけ）

`context`（ゲームのルールを英文で約150語：売り注文は翌日の価格で約定・±20%、需要→適正値、新聞は原因だけを書き一部誤報、
配信と防衛報酬、魔王軍支援、借金と破産、目的）＋盤面:

- `day` / `runProgress` / `cash` / `pendingSellOrdersValue` / `nextDebt{amount, daysLeft}`
- `shop{level, maxDisplayKinds, maxDisplayPerItem, blacksmithLevel, buzz}` / `hero{level, hp, attack, defense}`
- `nextStream{dungeon, dungeonLevel, daysUntil, heroClearChancePct}`
- `items[]`（解放済み＋在庫あり）: id・type・attribute・tier・price・basePrice・前日比・demand・需要の前日差・heat（MarketHeat の凪〜荒れ）・stock・maxStock・陳列数・配当
- `dungeons[]`: level・heroClearChancePct・defeatReward・supportCost
- `newspaper[]`: paper・page・source（confirmed/presumed/anonymous）・byline（signed/unsigned）・kind・**text = summaryEn**

**新聞は `summaryEn` 列を読ませる**（Docs/News_Spec.md §13。Jev は日本語で精度が落ちる）。空の記事は日本語見出しで代用し、要約を足すべき記事として把握できる。
未来の Trend・未購入の弱点属性・誤報フラグは載せない（プレイヤーが見られない情報）。

### 3.2 質問（営業日 = 1リクエストに最大6問）

| key | 型 | 内容 | 使い方 |
|---|---|---|---|
| `budget` | Score 6段階 | 今日、現金の何割を仕入れに使うか（0/20/40/60/80/100%） | score/5 を予算比率に |
| `buy` | Choice（買える銘柄、最大255） | 今日いちばん良い仕入れは | **確率分布で予算を配分**（上位4・シェア8%未満は捨てて正規化）。argmax にしない |
| `display` | Choice（在庫あり＋買える銘柄） | 棚に一番置くべき銘柄 | 確率順に陳列枠まで詰める |
| `display_amount` | Score 5段階 | 在庫のどれだけを陳列するか（残りは持ち越し・配信用） | 比率 |
| `support` | Choice（none＋払えるダンジョン） | 魔王軍支援するか・どこに | Sample=分布から抽選 / Argmax |
| `upgrade` | Choice（none/blacksmith/shop） | 設備投資するか | 同上 |

配信日: `stream_pick`（Choice・在庫あり銘柄・分布で上位6銘柄を選ぶ）＋ `stream_amount`（Score 5段階）。
＋ 介入（§2.5）: `stream_intervene`（Choice: none ＋ 払える介入 heal / skill / special / trap / curse / reinforce / bossbuff。説明に価格と効果を英語で）と
`intervene_timing`（Score 5段階: 開幕/序盤/中盤/終盤/最後＝ボス付近 → timing 0/0.25/0.5/0.75/1）。1件目は Sample/Argmax、2番手の確率が 25% 以上なら timing+0.3 で重ねる。
state の `stream` に 勇者の HP/攻撃/防御（装備込み）・表示クリア確率・魔物数・ボス有無・防衛報酬・所持金 を載せる。
レリック: `relic`（Choice・候補＋decline。説明は日本語しか無いのでレア度と ID が主）。

**分布を使う理由**: 市場シミュの教訓（argmax だと全員同じ手になり分布の情報が消える）。配分系はそのまま比率に、
1択系はシード固定の抽選（同じ盤面でも揺らぐ人間らしさ）にする。判断はすべて `decisions.csv` に確率分布ごと残る。

失敗（HTTP エラー・429 の打ち止め等）時はその判断だけ貪欲ボットで代替し `jev_error` として記録、ランは続ける。

### 3.3 ペルソナ（instructions の頭に付ける）

| id | 表示 | 方針 |
|---|---|---|
| steady | 堅実 | 破産回避最優先・返済額を残す・分散・無署名の噂は信じない |
| speculator | 投機 | 新聞の原因から値上がりを読み、1〜2銘柄に集中。損失を許容 |
| streamer | 配信重視 | 配信日に向けて仕入れて溜める。店売りは二の次。配信中は勇者側スパチャ（回復・スキル・必殺技）を好む |
| demonlord | 魔王軍支援 | 防衛報酬が費用を上回るなら次の配信ダンジョンを強化。配信中はダンジョン側の介入（罠・呪い・増援・ボス強化）を好む |

（堅実は「明らかに得なときだけ」、投機は「勝敗を振れるなら一発勝負」の文言を介入についても足してある）

ペルソナ × シードの全組み合わせを1バッチで交互実行する。比較の基準として毎回 貪欲・ランダムを同じシードで並走させる。

### 3.4 コスト見積

1リクエスト ≒ 3〜5k 入力トークン（state 2〜3k ＋ 選択肢の説明 1〜2k）。入力 $0.042/1M、出力無料。

| モード | 営業日 | 配信 | レリック | リクエスト/ラン | トークン | 1ラン |
|---|---|---|---|---|---|---|
| Short | 〜6 | 〜2 | 〜3 | 〜11 | 〜45k | **約 $0.002（0.3円）** |
| Medium | 〜16 | 〜5 | 〜6 | 〜27 | 〜110k | **約 $0.005（0.7円）** |
| Long | 〜30 | 〜8 | 〜10 | 〜48 | 〜190k | **約 $0.008（1.2円）** |

例: 4ペルソナ × 20シード × Medium ≒ 80ラン ≒ **約60円**。ウィンドウに概算が出る。実測値は summary に出る（`usage.input_tokens` を合算）。
レート制限 1200 req/分に対して、同時8ラン × 1req/〜0.4秒 なので余裕あり（429 は JevApi が指数バックオフ）。

**実測（2026-10-05）**: Medium 80ラン（4ペルソナ×20シード）で 1,654 リクエスト・入力 7.99M tokens（≒4.8k/req）・**$0.336（≒50円）**。
介入の質問は配信日のリクエストに相乗りする（リクエスト数は増えない）。メニュー説明のぶん配信日の入力が +0.5〜1k tokens 増える見込み → **同条件で約55円**。
対照群は貪欲・ランダムだけなら無料。Jev を含めると約2倍（`ControlRunsIncludeJev`）。`CostLimitUsd`（既定 ≒100円）で自動停止する。

**介入の試走の推奨条件**: Medium・20シード・4ペルソナ・貪欲/ランダム＋対照群（Jev 対照なし）・視聴者赤スパ FollowSettings。
まず API なしで貪欲/ランダムだけ回して ROI・反転率の桁を見る → 問題なければ Jev（≒55円）。
較正値（`SecondsPerBattleTurn` 3秒・`ViewerRedChancePerTurn` 0.03）は実配信の記録で見直す。

---

## 4. 出力

`<プロジェクト>/AutoPlayReports/<yyyyMMdd_HHmmss>_<Mode>/`（.gitignore 済み）

| ファイル | 中身 |
|---|---|
| `runs.csv` | 1ラン1行: 結果（完走/破産/停滞/例外）・最終/最大/最小所持金・純資産・配信回数・勇者の勝敗・最終Lv・営業入金・配信売上・防衛報酬・仕入れ・返済・支援費・却下手・異常件数・ログ件数・Jev リクエスト/トークン/コスト・所要秒 |
| `turns.csv` | 1日1行: 所持金・在庫評価額・未約定の売り注文・純資産・その日の支出/入金/配信売上/防衛報酬/返済/支援/投資・勝敗・クリア確率・勇者Lv・バズ・陳列種類数 |
| `decisions.csv` | 判断1件1行: 質問・選択・score・confidence・確率分布（上位8） |
| `anomalies.csv` | 異常1件1行 |
| `streams.csv` | 配信1回1行: 表示クリア確率・介入なしの勝敗・実際の勝敗・予定/実行/見送り（理由）・勇者側/ダンジョン側の支出・返金・必殺技（うち視聴者）・配信売上・防衛報酬・ターン数（介入なしのターン数） |
| `summary.json` | ボット別集計（破産率・所持金の平均/中央値/P10/P90・勇者勝率・判断の頻度・ターン別平均所持金）＋設定 |
| `summary.md` | 上記の表＋所持金推移表＋**配信中の介入**（介入/ラン・支出・返金・勇者側/ダンジョン側・介入あり/なしの配信の勇者勝率・反転率・ダンジョン側で負けに反転させた回数と防衛報酬・**ROI＝反転させた配信の防衛報酬 ÷ ダンジョン側支出**・種類別の実行回数・対照群との勝率/純資産の差）＋異常の種類別件数と例＋判断の分布＋警告ログの抜粋 |

runs.csv にも介入の列（回数・支出・返金・勇者側/ダンジョン側・反転回数・反転の防衛報酬・ダンジョン側配信の防衛報酬）を足した。turns.csv に `interventionNet`。

### 4.1 異常検知

| kind | 条件 |
|---|---|
| `negative_money` | 所持金 < 0 |
| `negative_stock` / `stock_over_max` / `display_over_stock` | 在庫の不整合 |
| `display_kinds_over` | 陳列種類数が店レベルの上限超え |
| `invalid_price` / `demand_out_of_range` | 価格 ≤ 0、需要が NaN・範囲外 |
| `invalid_sell_order` / `sell_order_overdue` | 売り注文の数量/価格不正、約定予定日を過ぎた注文が残っている |
| `turn_not_advanced` | 日送りしたのにフロー位置が変わらない |
| `clearprob_mismatch` | 表示クリア確率（≥50%=勝てる見込み）とドライランの勝敗が食い違う |
| `log_error` / `log_exception` | ラン中にゲーム本体が LogError / 例外を出した（警告は件数だけ） |
| `exception` | 操作中の例外（ランは Crashed） |
| `stalled` / `stream_failed` / `end_day_failed` / `sales_failed` | 進行不能 |

### 4.2 合格ライン（暫定・2026-10-05 司令官設定）

ボット別に判定し、summary.md の「合格ライン（暫定）の判定」表と summary.json の `Verdicts` に OK/NG を出す。
値は GameConst ではなく AutoPlay の設定 `AutoPlayBatchConfig.Criteria`（`AutoPlayPassCriteria`）で変える。

| 項目 | 基準 | 意図 |
|---|---|---|
| 破産率 | ≤ 15% | 普通に遊んで破産するのは稀であるべき |
| 勇者勝率 | 40〜75% | 魔王軍支援→防衛報酬の戦略が成立する範囲 |
| 最終純資産の中央値 | ≥ 初期資金 × 1.5 | ランを通して資産が増える手応え（P10 が初期資金割れは許容、破産は不可） |
| `negative_money` / `clearprob_mismatch` | 0 件 | 所持金のマイナス・表示確率と勝敗の食い違いはバグ扱い |

初期資金は本番（サーバー配信）と同じ **100,000G** を既定にした（`AutoPlayBatchConfig.InitMoneyOverride`。GameConst のローカル値 10,000 は変えない）。
Jev の累計コストが `CostLimitUsd`（既定 ≒100円）を超えたらバッチを自動で止める。

---

## 5. 使い方

- **ウィンドウ**: `Tools/TomsLands/Jev AutoPlay`。モード・シード数・ボット（貪欲/ランダム/Jev 4ペルソナ）・配信サロゲートの倍率を選んで「実行」。
- **クイック**: `Tools/TomsLands/Jev AutoPlay クイック試走（貪欲+ランダム×3シード・Short）`（API キー不要）。
- **unity CLI から**（MCP が落ちていても可）:
  `unity command eval --caller plugin --skill unity-cli --format json 'AutoPlayBatch.Start(new AutoPlayBatchConfig{ Mode=GameModeId.Short, Seeds=5 }); return "started";'`
  eval は即座に戻る（バッチは async で進む）。完了は `AutoPlayBatch.IsRunning` / `AutoPlayBatch.LastReportDir` をポーリングするか、
  `AutoPlayReports/` に `summary.md` が出るのを待つ。メニューは `ExecuteMenuItem` でも叩ける。
- **エディットモードで Addressables が読めない場合**はエラーで止まる → TitleScene を Play してから実行（タイトルはゲームのモデルを作らないので干渉しない）。
- 実行中にスクリプトを再コンパイルするとランは消える（ドメインリロード）。

---

## 6. 本体コードへのフック（最小・既定挙動不変）

| ファイル | 変更 | 理由 |
|---|---|---|
| `Assets/Scripts/Save/SaveSlotManager.cs` | `#if UNITY_EDITOR` の静的 `DevRootOverride`（既定 null）。`SlotRoot` は値があればそれを返す | 全モデルのセーブが `SaveSlotManager.GetPath` 経由なので、ここ1か所でユーザーのスロットを汚さずに隔離できる |
| `Assets/Scripts/SceneData/SceneTransitionService.cs` | 各 `SceneManager.LoadScene` を private `LoadScene` 経由に。`#if UNITY_EDITOR` の静的 `DevSceneLoadInterceptor`（既定 null）が true を返したらロードしない | 日送りが ResultScene / GameOver / FightScene へ遷移する瞬間を、実際のシーンロード無しに検知するため |
| `.gitignore` | `/AutoPlayReports/` | レポートをコミットしない |

どちらも `#if UNITY_EDITOR` で囲み、製品ビルドには入らない。null の間は従来とバイト単位で同じ動作。

---

## 7. 既知の割り切り（MVP）

- 配信はサロゲート（§2.4）。配信熱・BattleDemandTracker・補充・属性相性の値動きは無し。
- 新聞の**購読制は本体が未実装**なので、紙面全体が見える前提（購読を実装したら `subscribe` の Choice を足す）。
- 情報屋（弱点の購入）・広告・金融商品・マシン・勇者の装備変更はまだボットの手に無い（API に足せば Jev の質問も足せる）。
- 「営業日の計画は1リクエスト」なので、仕入れの結果を見てから陳列を考え直すことはしない（陳列は買う予定の銘柄も候補に含める）。
- レリックの説明文は日本語のみ（英訳列が無い）。
- 介入（§2.5）: 配信中に戦況を見て判断し直すことはしない（予定リストを配信前に1回決めるだけ）。熱・同接・武器の需要UP/価格UP（スキル・必殺技の副作用）、
  勇者のタップ指定（対象選択。総ダメージは同じ）、カットインの時間は再現しない。視聴者スパチャは赤スパの必殺技だけ。
  介入した配信の「反転」は介入なしドライランとの比較なので、同じ配信で視聴者の必殺技が出た影響も含まれる（streams.csv の viewerSpecials で区別できる）。
- 写しの追従: `InterventionCommandQueue.ResolveAttack`・各 Command・`BattleSceneStarter` の精算式が変わったら `AutoPlayBattleSurrogate` を直す。
  `BattleOutputData.SetStreamingStats` を呼んでいるので、その引数が変わったら `AutoPlayHeadlessGame.StartStream` も直す。

---

## 8. 実装フェーズ

| Phase | 内容 | 状態 |
|---|---|---|
| 1 MVP | IPlayerActions・ヘッドレス本体・サロゲート・貪欲/ランダム/Jev(4ペルソナ)・交互実行・レポート・異常検知・EditorWindow | **実装済み（コンパイル・試走は未確認）** |
| 2 手を増やす | 情報屋（弱点購入→DungeonIntelModel）・広告・新聞購読（実装後）・金融・マシン・寄り道仕入れ（配信前の鍛冶屋）。Jev に `info` / `ad` / `subscribe` を追加 | 未着手 |
| 3 Play モード版 | 同じ `IPlayerActions` を実シーン上で実装（Battle/ の作業が落ち着いてから、FightScene に品出し確定・補充・リザルト送りの操作口を足す）。実配信を記録して `StreamSalesScale` を較正 | 未着手 |
| 4 回帰テスト化 | 貪欲・ランダムの固定シード結果を基準値として保存し、差分が閾値を超えたら知らせる（バランス変更の影響を即見る） | 未着手 |
| 5 J1〜J4 | 新聞記事の解読可能性など News_Spec §13 の検証を同じ JevApi で | 未着手 |

---

## 9. 未決事項

1. **写しの追従**: Presenter 側（営業サマリー・返済・購入・魔王軍支援）が変わったとき写しがずれる。
   本体側を「Presenter から呼ぶ純粋関数」に切り出せば写しは消せる（例: `TurnEndSummaryPresenter.Entry` の売り注文化を `SellOrderModel` 側へ）。今回は本体に手を入れない方針なので保留。
2. **サロゲートの較正方法**: 実配信の売上ログをどう取るか（Phase 3 で Play モード版を作るか、FightScene にログ出力だけ足すか）。
3. ~~初期資金~~ → 100,000G で確定（§4.2）。
4. **Jev の判断の解釈**: 1択系を Sample にするか Argmax にするか（既定 Sample）。Argmax は再現性が高いが人間らしいばらつきが出ない。
5. **ペルソナの妥当性**: 4種で足りるか。「何も考えない新人」「新聞だけ読む人」等を足すか。
6. **評価指標**: 「バランスが良い」の合格ライン（例: 堅実の破産率 < 10%・貪欲が堅実より稼げない・勇者勝率 40〜70%）を決める。

---

## 10. 試走記録

### 2026-10-05 本番試走（Medium・20シード・初期資金100,000G）
レポート: `AutoPlayReports/20261005_091048_Medium`（6ボット×20シード）。Jev 1,654リクエスト・入力 7.99M tokens・**$0.336（≒50円）**。
配信の代用処理を修正したあと（ウェーブの周回に追従）、貪欲とランダムだけ再実行: `AutoPlayReports/20261005_091425_Medium`。

| ボット | 破産率 | 勇者勝率 | 純資産中央値 | 合否 |
|---|---|---|---|---|
| greedy | 0% | 98% | 206k（×2.06） | 不合格（勝率） |
| random | 30% | 96% | 152k（×1.52） | 不合格（破産・勝率） |
| jev_steady | 0% | 98% | 186k（×1.86） | 不合格（勝率） |
| jev_speculator | 10% | 96% | 92k（×0.92） | 不合格 |
| jev_streamer | 0% | 96% | 128k（×1.28） | 不合格 |
| jev_demonlord | 10% | 94% | 65k（×0.65） | 不合格 |

所見: 勇者が勝ちすぎ（魔王城以外は全レベル勝率100%）。魔王軍支援は元が取れない（demonlord の支援費は平均99k、防衛報酬は平均22k）。
破産はすべて「現金不足・在庫過多」（純資産10万〜25万あるのに現金2〜6千）。強制返済の救済が金融資産だけで、在庫は対象外なのが原因。

### 2026-10-05 介入入りの試走（Medium・20シード・初期資金100,000G・視聴者赤スパは SO に従う）
- ベースライン（貪欲/ランダム＋介入なし対照群・API なし）: `AutoPlayReports/20261005_140436_Medium`
- Jev 4ペルソナ（対照なし）: `AutoPlayReports/20261005_140537_Medium`（1,664 req・8.42M tokens・**$0.354 ≒ 53円**）

**介入で勝敗が反転した配信は 0 件**（Jev 120配信・ランダム 12配信）。ダンジョン側 ROI は全ボットで 0。
原因はダメージ式 `max(1, 攻撃 − 防御)`: Lv1〜3 のダンジョンでは魔物の攻撃が勇者の防御以下で、1発1ダメージしか通らない（勇者は HP 90〜100% 残して勝つ）。
そのため倍率系（ボス強化 攻撃×1.25・呪い 勇者攻撃×0.7）は効かず、効くのは防御無視の罠（最大HP×10%）だけ。
勇者側も、もともと勝っているので スキル・必殺技 は意味がなく、回復は「戦闘が延びて配信売上が増える」用途でのみ得になる（貪欲が魔王城で使用）。
効果量の行列（勇者Lv×ダンジョンLv×介入）は eval のドライランで確認（4種同時でも反転するのは魔王城 Lv5×勇者Lv5 と忘却の霊廟 Lv3×勇者Lv1 だけ）。

### 2026-10-05 ダンジョン側介入の効果量の再調整（金額は据え置き）
変更: 罠 最大HP×10%→**30%** / 呪い 勇者の攻撃×0.7（2回）→**勇者の防御×0.5（被弾9回）** / ボス強化に**防御貫通50%**を追加（攻撃×1.25・被ダメ×0.8 は据え置き）。
本体（`InterventionCommandQueue.ResolveAttack`・`CurseCommand`・`StreamingInteractionSettings`＋SO アセット）と AutoPlay の写しを同時に変更。

API なしの試走（Medium・20シード・初期資金100,000G）。ダンジョン全振りボットで値を振った結果:

| 設定 | 勇者勝率 | 反転率（介入した配信のうち勝ち→負け） | 反転の防衛報酬 | ROI |
|---|---|---|---|---|
| 旧（罠10%・呪いの効果なし・貫通0） | 95% | 3%（2回） | 20,000 | 0.01 |
| 罠25%・呪い×0.5×9・貫通50% | 92% | 5%（4回） | 40,000 | 0.01 |
| **採用: 罠30%・呪い×0.5×9・貫通50%** | **89%** | **9%（7回）** | 70,000 | 0.03 |
| 罠30%・呪い×0.5×9・貫通100% | 89% | 9% | 70,000 | 0.03 |

貪欲・ランダム（＋介入なし対照群）は変更前と同じ結果（配信前に現金が残っておらず、ダンジョン側をほとんど使わないため）。
ROI が低いのはダンジョン Lv1 の防衛報酬が 10,000G しかないため（Lv4 80,000G・Lv5 200,000G）。魔王軍支援でレベルを上げた配信と組み合わせた検証が次の課題。
レポート: `AutoPlayReports/20261005_164005_Medium`（旧）/ `_164015`（25%）/ `_164024`（30%・貫通100%）/ `_164108`（採用値・全ボット）。

### 2026-10-05 防衛報酬狙い（魔王軍支援 × ダンジョン側介入）の検証
ボット `AutoPlayDungeonBot(variant)`: `s{N}` = 次の配信ダンジョンを Lv N まで魔王軍支援、`i` = 配信でボス強化→呪い→罠×3 を重ねる。
店は貪欲と同じで、配信の3日前から仕入れを控えて支援・介入の現金を貯める（前日だけでは貪欲が毎日使い切るので足りなかった）。
設定 `AutoPlayBatchConfig.UseDungeonBot` / `DungeonBotVariants`。レポートに「防衛報酬狙いの収支」節（ROI = 防衛報酬 ÷ (支援費 + 介入の純支出)）。

API なし・Medium・20シード・初期資金100,000G（`AutoPlayReports/20261005_164856_Medium`）:

| 型 | 勇者勝率 | 防衛報酬 | 支援費 | 介入 純支出 | ROI | ROI>1 のラン | 純資産中央値 | 破産率 |
|---|---|---|---|---|---|---|---|---|
| 貪欲（参考） | 98% | 20k | 0 | 10k | – | 0/20 | 206k | 0% |
| i 介入のみ | 89% | 90k | 0 | 2,850k | 0.03 | 0/20 | 14k | 5% |
| s3 支援のみ Lv3 | 92% | 240k | 855k | 0 | 0.28 | 1/20 | 165k | 5% |
| s4 支援のみ Lv4 | 92% | 440k | 1,615k | 0 | 0.27 | 1/20 | 111k | 10% |
| **s5 支援のみ Lv5** | 72% | 4,000k | 2,925k | 0 | **1.37** | **11/20** | **260k** | 5% |
| s3i 支援Lv3＋介入 | 66% | 1,022k | 770k | 2,670k | 0.30 | 0/20 | 46k | 25% |
| s4i 支援Lv4＋介入 | 54% | 2,584k | 1,500k | 2,770k | 0.61 | 1/20 | 78k | 25% |
| **s5i 支援Lv5＋介入** | 33% | 8,244k | 2,655k | 3,400k | **1.36** | **14/20** | **345k** | 15% |

配信の勝敗（ダンジョンLv別）: Lv5 では支援のみで 19/42 敗北、介入を重ねると 35/36 敗北。Lv4 は支援のみ 5/52・介入あり 28/47、Lv3 は 6/76・24/63。

勇者の勝ち数（6ダンジョン中。セル = 介入なし / 罠×3 / 全部重ね）:

| ダンジョンLv | 勇者Lv1 | Lv2 | Lv3 | Lv4 | Lv5 |
|---|---|---|---|---|---|
| Lv1 | 5/5/4 | 6/5/5 | 6/5/5 | 6/6/5 | 6/6/5 |
| Lv2 | 5/5/0 | 5/5/5 | 6/5/5 | 6/5/5 | 6/6/5 |
| Lv3 | 5/4/0 | 5/4/0 | 5/4/4 | 6/5/4 | 6/5/5 |
| Lv4 | 5/0/0 | 5/0/0 | 5/5/0 | 6/5/0 | 6/5/5 |
| Lv5 | 0/0/0 | 1/0/0 | 5/0/0 | 5/0/0 | 6/5/0 |

**判定**: 「Lv5 まで育てた場合に限って」ハイリスク・ハイリターンで元が取れる（ROI≒1.4、7割のランで黒字、純資産は貪欲の 1.3〜1.7倍、破産 5〜15%）。
Lv3〜4 への投資と介入だけの戦略は、どの組み合わせでも元が取れない（ROI 0.03〜0.6）。原因は防衛報酬の段差（Lv4 80k → Lv5 200k の崖）と、Lv4 以下では勇者がほぼ負けないこと。
