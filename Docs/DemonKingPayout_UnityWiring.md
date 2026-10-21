# 魔王軍の配当通知書 Unityエディタ配線手順

勇者が負けたとき（＝魔王側の配当が確定したとき）、配信リザルトのあとに「配当通知書」を出して防衛報酬の内訳を見せる。
その前に、配信画面のまま紫の帯「配当確定」の溜めが入り、配信リザルトの見出しも「配当確定」になる（Streaming_Redesign.md §9-1・§9-2）。
デザインはモックアップ案B（`Docs/mockups/payout_B_dividend.png`）。実装のキャプチャは `Docs/mockups/payout_B_impl.png`。
**未配線でもコンパイル・起動し、従来どおりリザルト → 店に戻る**（通知書を飛ばすだけ）。

## 仕様の要点

- 出す条件: `BattleResult.Defeat` かつ防衛報酬 > 0。勝ったとき・報酬 0 のときは今までどおり。
- 流れ: Phase 3（`BattleResultView.ShowResultAsync`）→ **Phase 3.5 配当通知書** → Phase 4（`ReturnToTomsShop`）。
- お金は変えない。防衛報酬はすでに `totalEarnings` に入っているので、この画面は表示だけ（二重加算なし）。
- 内訳は `BattleSceneStarter.CalculateDefeatReward` が返す `DefeatRewardBreakdown`（基本額・倍率・受取額）から作る。
  受取額は `totalEarnings` に入れている値と同じ変数なので、計算は1か所だけ。
- 表示する内容:
  | 行 | 値 | 出す条件 |
  |---|---|---|
  | ダンジョン名 Lv | `DungeonInfoScriptableObj.dungeonName` と `BattleInputData.DungeonLevel` | 常に |
  | 支援額 | 魔王軍支援でこのダンジョンに払った累計（下記） | 支援額 > 0 |
  | 防衛報酬 | `GetLevelData(Lv).rewardGold`（基本額） | 常に |
  | レリック補正 ×N | 受取額 − 基本額 | 倍率 > 1.0 |
  | 受取額 | 最終の防衛報酬（金の大きな数字、カウントアップ） | 常に |
  | 回収倍率ハンコ | **受取累計** ÷ 支援額（例「2.0倍」）。はじめて累計が支援額に届いた回は「元本回収」 | 支援額 > 0 |
  | 魔王の吹き出し | のじゃ口調のセリフ5種からランダム（`DemonKingPayoutView.DemonKingLines`） | 常に |
- 出さない行があるときは、残りの明細行を明細欄の中央に詰めて並べ直す。
- 受取累計はダンジョンごとに `slot_N/demonPayoutHistory.json`（`DemonPayoutHistory`）。帰還処理（`BattleResultHandler`）で今回分を足して保存し、
  通知書は「これまでの累計 + 今回」を出す（今回分を二重に足さない）。ラン内データなので `RunSaveCleaner` で消える。
- 魔王側で当てたときもレリック3択を積む（勝利時と同じ）。トコの帰還台詞は `Toko_StreamDemonPayout_1/2`。

### 支援額の出し方

支払額そのものはセーブしていない。ダンジョンLvは魔王軍支援（`DungeonLevelUpPresenter.HandleLevelUp`）でしか上がらないので、
**初期Lv（`initDungeonLevel`）→ 現在Lv のレベルアップ費用（`DungeonLevelData.levelUpCost`。そのLvから次のLvへの費用）の合計**で出す
（`BattleSceneStarter.CalculateSupportAmount`）。セーブ形式は変えていない。

ずれるケース:
- デバッグメニュー（F11）の Lv ±1 で上げ下げしたとき（払っていないのに支援額が出る）
- 途中で `balance.json`（RemoteBalance）の `levelUpCost` が変わったとき（今の表の値で計算する）

## 1. 配置（FightScene、配線済み）

1. `Assets/Prefabs/Screens/DemonKingPayout.prefab` を FightScene のルートに置く（配置済み）。
2. `LifetimeScope`（`BattleLifetimeScope`）の Inspector →「配当通知書（勇者敗北時。未設定なら出さない）」→
   **Demon King Payout View** にシーン上の `DemonKingPayout` を割り当てる（配線済み）。
   - 未設定なら登録しない。`BattleSceneStarter` は `IObjectResolver.TryResolve` で取るので解決エラーにならない。

## 2. プレハブ構成

`DemonKingPayout`（`DemonKingPayoutView`）
- `Canvas`（**panelRoot。初期非アクティブ**。ScreenSpaceOverlay / sortingOrder 50 / 1920x1080・幅合わせ）
  - `Overlay` … 全面の暗い半透明（配信画面の上に重ねる。下の操作をブロック）
  - `DemonMask`（RectMask2D・ソフトエッジ）> `Demon` … `Assets/UI/Character/魔王-背景透け.png` を拡大して胸から上を見せる
  - `Bubble`（`戦闘/3フキダシ右向き`）> `Text`
  - `Window`（windowRoot）
    - `Frame`（`新聞/1枠`）/ `Paper`（`新聞/8紙面ベース`）
    - `TitleSign`（`追加/文字なし0−0`）> `Title`「配当通知書」
    - `DungeonText` / `Ornament`・`Separator`（`マップ/ダンジョン詳細/9線`）
    - `支援額Row` / `防衛報酬Row` / `レリック補正Row`（CanvasGroup。Label・Leader（点線）・Value）
    - `TotalRow`（CanvasGroup。Label「受取額」・Value 金文字）
    - `Seal`（封蝋）/ `Stamp`（`リザルト/ハンコ枠` を赤く着色）> `Text`「2.0倍」
  - `ReceiveButton`（`新聞/7購読枠プレート`）> `Label`「受け取る →」
  - `SkipCatcher` … 演出中だけ有効な全面の透明ボタン（クリックで演出スキップ）

新規素材（`Assets/UI/配当通知書/`）:
- `封蝋.png` … 赤い蝋に角と目玉の紋章（image-gen で生成 → クロマキーで透過）
- `点線.png` … 明細のリーダー点線（Tiled 用の小さなドット）
- `MPLUS_配当通知書_金縁.mat` … 金文字（看板タイトル・受取額）用の TMP マテリアル（濃茶の縁＋落ち影。`MPLUSRounded1c-Bold SDFPlusPadding` ベース）

## 3. 演出

`ShowAsync(DemonKingPayoutData)` が「受け取る」まで await する。
1. 通知書が開く（`UIFx.PanelOpen` + Window を `UIFx.Pop`、魔王は右からスライドイン）
2. 明細の行が順に出る（フェード + Pop）
3. 受取額がカウントアップ（DOTween.To、0.8秒）
4. 封蝋 → ハンコが「ポン」（`UIFx.Pop` を拡大から）
   - 音: カウントアップの開始で `営業/SE_数の増減`、確定で `営業/SE_売上音`（スキップしても確定音は1回だけ）。ボタンの押下音は ButtonSoundHandler 側
5. 吹き出し → 「受け取る →」ボタン

- 演出中のクリック（パッドは B）でスキップして最終状態にする。Tween は全部 `SetLink(gameObject)`。
- 設定「演出を速くする」で待ちとカウントアップが半分になる。
- パッド/キー: `UIFocusScope`（Modal / Priority 200）で受け取るボタンにフォーカス。

## 4. オートプレイ

`DebugAutoPlayer` は `DemonKingPayoutView.IsShowing` を見て `ReceiveImmediately()` を呼ぶ（配信リザルトより先に判定）。

## 5. 動作確認

- Play 中に `DemonKingPayout` を選び、`DemonKingPayoutView` の右クリックメニュー **「Debug: ダミー表示」**
  （魔王城 Lv.3 / 支援 30,000 / 防衛 50,000 / ×1.2 / 受取 60,000）で見た目と演出を確認できる。
- チェックリスト:
  - [ ] 勇者敗北 & 防衛報酬 > 0 → リザルトのあとに通知書 → 「受け取る」で店に戻る
  - [ ] 勇者勝利 / 防衛報酬 0 → 通知書は出ずに今までどおり店に戻る
  - [ ] 店に戻ったあとの所持金が従来と同じ（通知書で増えない）
  - [ ] 支援していないダンジョン → 支援額の行とハンコが出ない
  - [ ] レリック補正なし → レリック補正の行が出ない
  - [ ] 演出中クリックでスキップできる

## 6. ダンジョン Lv0（未出資）— 2026-10 レビュー1
- ダンジョンは **Lv0（未出資）** から始まる（`initDungeonLevel` = 0。SO・`Docs/balance_upload/balance.json`・`Docs/balance_tsv/dungeons.tsv`）。魔王軍支援で出資すると Lv1。
- Lv0 は防衛報酬 0（`DungeonLevels.DefenseReward`）。配当通知書・「配当確定」の溜め・見出し・トコの配当台詞・Steam 実績 DEMON_DIVIDEND は防衛報酬 > 0 のときだけなので、Lv0 では出ない。
- Lv0 の戦闘（敵の出方・クリア見込み）は Lv1 と同じ（`GetLevelData` が 1〜5 にクランプ）。
- 介入の値段（防衛報酬に比例）は Lv1 の防衛報酬を下限にする（`BattleSceneStarter.InterventionPriceBase`）。
- 最初の出資（Lv0→Lv1）の費用は Lv1 の行の `levelUpCost`（10,000G）。
- 表示: 魔王軍支援・地図は「Lv.0（未出資）」、地図の防衛報酬は「なし（未出資）」、品出し画面のレベルは「0（未出資）」。
- 旧セーブ: `save.json` のダンジョンは `levelVersion` を持たない。旧形式の Lv1 は出資していない状態なので Lv0 として読み、Lv2 以上はそのまま。あわせて、保存のキーが日本語のダンジョン名で読み込みの `Enum.TryParse` に必ず外れていた不具合（レベルが毎回 SO の初期値に戻っていた）を直した（enum 名で保存し、日本語名でも読める）。

## 7. 「宣言的中！」の帯（村の広場 Lv5「勝負配信の宣言」）
- 宣言が当たった配信では、決着の溜め（「CLEAR!」／「配当確定」）のあとに、同じ帯の作りで「宣言的中！」をもう一度出す（`BattleWaveFxView.PlayBannerAsync`。勇者側は金、魔王側は紫）。素材・配線の追加はなし。
- 配信リザルトの内訳に6行目「宣言的中 ×1.5」（上乗せ額）／「宣言 外れ」／「宣言 無効」を出す（宣言した配信だけ）。仕様は `Docs/Village_Meta_Design.md` §0-6。
- 確認: `BattleWaveFxView` の右クリックメニュー「Debug: 宣言的中（勇者側）」「Debug: 宣言的中（魔王側）」。
