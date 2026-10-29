# 人ができる操作の洗い出し（2026-10-28）

## このメモについて

- **目的**: 人のプレイヤーがゲームの中でできる操作を、すべて書き出す。人格の自動プレイ（AutoPlay）に操作を足すとき、実況動画を企画するとき、ビルドごとに手で遊んで確かめるときに使う。
- **調べ方**: 推測では埋めていない。どれもコードで確かめた。見たのは次のとおり。
  - シーンと画面（View / Presenter）
  - ボタンの OnClick のつなぎ方
  - モデルの public メソッド
  - イベント・会話の選択肢
  - 設定値（解禁の条件、費用、上限）
  - ボタンの置き場所は、一部をプレハブとシーンの YAML でも確かめた。
- **数字の出どころ**: リポジトリに入っている値を書いた。`Assets/Resources_moved/*.asset`、`GameConstData.cs` の既定値、CSV がそれにあたる。多くの値はスプレッドシートから配信で上書きされる（RemoteBalance / gameconst.json）。本番の値は確かめていない。
- **人格の使用回数**: `Recordings/persona_log_*.jsonl` の 80 ファイルをユーザーが数えた値。判断の数は全部で 1,422 日ぶん。このメモを書くときにログは読んでいない。質問も自動操作もない操作は 0 回。
- **行番号**: パスは `Assets/Scripts` から。2026-10-28 時点の作業ツリーの行番号で、ずれることがある。
- **食い違い**: 設計書とコードが食い違うところは「設計書との食い違い」の節にまとめた。正しいのはコードのほう。

### 凡例

| 言葉 | 意味 |
|---|---|
| 自動操作 | Play 中の AutoPlay（`Debug/AutoPlay/AutoPlayController*.cs` と、エンジン役の `Debug/DebugAutoPlayer.cs`）が、その操作を実際にするか。「DAP」は DebugAutoPlayer、「APC」は AutoPlayController |
| 人格の質問 | 人格プレイがモデルに聞く質問のキー（`Debug/AutoPlay/Jev/PersonaRequests.cs`）。営業日は `BuildDayRequest` 220-274、配信は `BuildStreamRequest` 305-324、村は `BuildVillageRequest` 357-369 |
| 人格の使用回数 | 上のログでその質問が答えられた回数 |
| 旧ヘッドレス | 古いヘッドレスのボット（`Editor/AutoPlay/AutoPlayHeadlessGame.cs` / `AutoPlayContracts.cs` の `IPlayerActions`）にその手があるか。参考として書く。Play では使っていない |
| リフレクション名 | AutoPlay が `AutoPlayUi.GetField` で読む SerializeField / private フィールドの名前。正確に書いた。名前を変えると AutoPlay が壊れる |
| Subject | View が公開している R3 の通知。`OnNext` すると、ボタンを押したのと同じ処理が走る（押し込みの見た目は出ない） |
| 第1ラン | `MetaProgressModel.TotalRuns<=0`。チュートリアルの段階的な解禁（`Scenario/TutorialPresenter.cs` 279-312）が効くのはこのときだけ。第2ランからは、店ホームの機能ボタンがすべて出る |

---

## 人格が使っていない操作（優先度順）

人格のログでの使用回数は、どれも 0 回。

1. **勇者の装備・作戦・細工**（F1〜F4）
   - 武器・防具を変える
   - 作戦を選ぶ
   - 職人組合 Lv5 の細工（業物 / なまくら）
   - 勇者画面そのものを、Play の自動操作は一度も開かない。理由は二つ。配信前日の確認「明日は配信です」に、いつも「このまま営業する」と答える（DAP 511-518）。店ホームの勇者ボタンも押さない。
   - 武器の属性が敵の弱点なら攻撃 ×2 になる。なのに装備は初期のまま。
2. **開発**（E10 鍛冶屋 Lv、G4 情報屋 Lv）
   - 鍛冶屋 Lv を上げると、扱える銘柄・装備・お任せ仕入れの対象が増える。
   - 情報屋 Lv を上げると、金融商品の解禁と債券の口数の上限が上がる。
   - 開発タブは、どちらも開かない。
3. **店 Lv・設備**（J2 店 Lv、J4〜J7 設備）
   - 店 Lv は、陳列できる種類の数（3→12）と設備の枠を決める。
   - 店 Lv を上げないので、陳列はずっと 3 種のまま。
4. **投資信託・債券**（G6〜G8）
5. **その他**
   - 広告（J8・J9）
   - 新聞: 購読（I5）、スクラップ（I9）、予想メモ（I11）、確認（I12・I13）、裏取り（I14）、過去の号（I7）
   - 占い師（H6）
   - 自分から納付する（K2）
   - 勝負の融資（K4）・勝負配信の宣言（L2）。自動操作はいつも「使わない」と答える。
   - レリックの辞退（K7）
   - 出店準備
     - 難易度（C3）。いつも初期値のまま。
     - スタートダッシュ 3 種（C5〜C7）
     - 持ち込み資金の細かい調整（C2）。いつも上限まで持ち込む。
   - 配信中
     - 一時停止・倍速・途中で終える（M1〜M3）
     - 品出しの手動選択（L4〜L6）。いつもオート選択。
   - 陳列の手動調整（J12〜J15）
   - 投資信託の売却（G8）
   - 設備の撤去・作る品の変更（J6・J7）

自動で押しているが、人格に質問していない操作もある。下の表で「質問なし」と書いたもの。

| 操作 | 自動操作の決め方 |
|---|---|
| レリック3択（K6） | ずんだもんは乱数、めたんは先頭 |
| 補充の数（M11） | ずんだもんは既定数の 1.5 倍、めたんは既定数 |
| 配信日の朝に鍛冶屋へ寄るか（L1） | いつも寄る |
| 陳列（J11） | いつもオート陳列 |
| 持ち込み資金（C1） | いつも上限まで |
| スターターレリック（C4） | いつも先頭 |
| 地図を見る（H1） | めたんだけ、偶数の日に眺める |
| 棚の入れ替え（M5） | 空いた棚を、在庫×価格がいちばん大きい品で埋める |
| 値動きグラフ（M6） | 15% の確率で見る |
| 狙う敵（M10） | ボスを先に、いなければいちばん弱っている敵 |

---

## 一覧表

**全部で 138 操作**（M9 は介入の共通ルールの行、P 節は押せないものの記録なので数に入れない）（デバッグ専用は別の節に分けた）。

### A. タイトル・保存スロット・設定

| # | 操作（人の言葉で） | 画面・入口（View/Presenter とメソッド、ファイル:行） | 解禁条件 | 費用・上限 | ゲームへの効き方 | 自動操作 | 人格の質問 | 人格の使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| A1 | タイトルで「スタート」 | `TitleView.startButton`（Title/TitleView.cs:17）か任意キー（113-114）→ `TitlePresenter` 51-53 | いつでも | – | 始め方の画面へ | あり（APC `StepTitleAsync` 603-615） | なし | 質問なし | 判断がいらない | 注1 | なし |
| A2 | 初めから | `newGameButton`（:20）→ `TitlePresenter` 58-71 | いつでも | 3 スロット | スロット選択へ。保存が 1 つも無ければスロット1で即開始 | あり（設定 `TitleAction`、APC 587-601） | なし | 質問なし | 判断がいらない | 上書きの確認には、APC がいつも「戻る」と答える（880-896） | なし |
| A3 | 続きから | `continueButton`（:21）→ `TitlePresenter` 73-80 | 保存があるときだけ押せる（233-234） | – | スロット選択（読み込み） | あり（APC 587-601） | なし | 質問なし | 判断がいらない | – | なし |
| A4 | 保存スロットを選ぶ | `SaveSlotView.selectButton`（Title/SaveSlotView.cs:14）→ `TitlePresenter.OnSlotSelected` 194-207 | 読み込みは保存のあるスロットだけ | 3 スロット（Save/SaveSlotManager.cs:18） | 続きなら店か村へ。初めからは上書きの確認 | あり（APC 572-584。リフレクション名 `_slotIndex`・`selectButton`） | なし | 質問なし | 判断がいらない | 注2 | なし |
| A5 | 保存スロットを消す | `SaveSlotView.deleteButton`（:16）→ `TitlePresenter` 210-230 | データがあるとき | – | スロットのフォルダを丸ごと消す | なし（確認には必ず「戻る」と答える） | なし | 0 | 消してはいけない | 足さない | なし |
| A6 | オプションを開く | `optionButton`（:22）→ `TitlePresenter` 83-85, 108-128 | いつでも | – | 設定シーンを重ねて開く | なし | なし | 0 | 遊びに関係ない | – | なし |
| A7 | ゲームを終える | `quitButton`（:24）→ `RequestQuit` 134-160 | いつでも | – | 確認のあとアプリを終える | なし | なし | 0 | – | 足さない | なし |
| A8 | 戻る | `startMethodBackButton` / `saveDataBackButton`（:25, :36）→ `GoBack` 162-173 | – | – | 前の画面へ | なし | なし | 0 | – | – | なし |
| A9 | 設定: タブ切り替え・BGM/SE 音量・表示モード・解像度・演出を速く・揺れを減らす・文字速度・オート送り・既読スキップ | `Setting/Setting.cs`。タブ 45-49、`bgmSlider`/`seSlider` 21-22、`displayModeDropdown` 28、`resolutionDropdown` 29、`fastEffectsToggle`/`reduceShakeToggle` 32-33、`textSpeedSlider`/`autoAdvanceToggle`/`skipReadToggle` 37-39 | いつでも（店ホームは歯車・Start、配信中も開ける） | – | 演出を速くすると演出時間が 0.5 倍（Setting/GameSettings.cs:56-68）。ほかは見た目と音だけ | なし | なし | 0 | 遊びの判断ではない | 録画で「演出を速く」を固定したいなら `GameSettings.FastEffects` | なし |
| A10 | 設定からタイトルへ戻る | `toTitleBtn`（Setting.cs:64）→ `RequestReturnToTitle` 457-495 | タイトルと配信中は出ない | – | 店の状態を保存してタイトルへ | なし | なし | 0 | – | 足さない | なし |

### B. 村（ランの合間）

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| B1 | 村を歩く | `PlayerMove.OnMove`（TomsShop/PlayerMove.cs:39-44） | 到着の演出・会話の間は動けない | – | 施設に近づくと吹き出しが出る | なし（歩かずに区画を直接「調べる」） | なし | 質問なし | 歩かなくても調べられる | 動画で歩かせたいなら物理移動が必要 | なし |
| B2 | 施設を調べる（投資パネルを開く） | `FacilityPlot`。E/Enter/Space/パッドの South、またはクリック（Village/FacilityPlot.cs:101-121）→ `OnInteract`（Subject<string>:38）→ `VillagePresenter.SelectFacility` 217-234 | 演出中（`IsBusy`）は開かない。区画は 7 つ | – | 投資パネル（いまの効果・次の効果・費用） | あり（APC `TourVillageAsync` 692-725。`plot.OnInteract.OnNext`） | なし | 質問なし | – | `IAutoPlayJikkyoExtras.VillageLookSeconds>0` の戦略だけが回る | なし |
| B3 | 施設に投資する（建設・1 Lv 拡張） | `VillageView.investButton`（Village/VillageView.cs:41）→ `OnInvest` → `VillagePresenter.HandleInvest` 264-292 → `VillageModel.TryInvest` 90-114 | 領主館 Lv N なら、ほかの施設は Lv N+1 まで（Lv1 は条件なし）。村資金が足りること | Lv1〜5 の費用は注3。最大 Lv5。6 施設ぜんぶで 1 億 3,800 万G | 次のランから効果がずっと残る（注3） | あり（APC 714-719。`investButton`） | village_budget / invest_<施設ID> | village_budget 84 回、invest 289 回（投資 83 / 見送り 206） | – | 済み | なし |
| B4 | 投資パネルを閉じる | `investCloseButton`（:43） | – | – | – | あり（APC 657, 720-721） | なし | 質問なし | – | – | なし |
| B5 | 帰還報告（換金の知らせ）を閉じる | `conversionPopup` / `conversionCloseButton`（:46, :50） | 到着の演出が出せなかったときだけ出る | – | 見るだけ。お金は村に着く前に入っている | あり（APC 654-655） | なし | 質問なし | 判断がいらない | – | なし |
| B6 | 到着の演出を飛ばす | 何かのキー・クリック（Village/VillageArrivalFx.cs:373-386） | 0.2 秒たってから | – | 演出だけ | なし（終わるのを待つ） | なし | 0 | – | `VillageArrivalFx.Skip()`（public） | なし |
| B7 | 村の格が上がった知らせを閉じる | `conversionPopup` を使い回す（VillageView.cs:265-285）→ `conversionCloseButton` | 投資で格が上がったとき | – | 格: 開拓地→村（総合Lv2）→町（総合Lv10・領主館Lv1）→王都（総合Lv30）。難易度の解禁につながる | あり（APC 742-746） | なし | 質問なし | – | – | なし |
| B8 | 自己ベストの掲示板を見る | 区画 `records` → `ShowRecordBoard` 404-423、`recordBoardCloseButton`（:56） | 格が王都のときだけ | – | 見るだけ | なし | なし | 0 | 見るだけ | – | なし |
| B9 | 出店準備へ | `departButton`（:22）→ `OnDepart` → `VillagePresenter` 131-136 | 演出中は受け付けない | – | 出店準備へ | あり（APC 682-688。受け付けるまで押し直す） | なし | 質問なし | – | – | なし |
| B10 | 村からタイトルへ | `titleButton`（:23）→ `VillagePresenter` 138-142 | 演出中は受け付けない | – | タイトルへ | なし | なし | 0 | – | – | なし |

### C. 出店準備

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| C1 | 持ち込み資金を ＋ / − | `PreparationView.borrowPlusButton` / `borrowMinusButton`（Preparation/PreparationView.cs:28-29）→ `PreparationPresenter` 130-131 → `AddCarry` / `SubtractCarry` | – | 1 回 10,000G（PreparationModel.cs:10）。上限は「村資金」と「銀行 Lv ごとの上限 5万/10万/20万/40万/70万」の小さいほう | 店を開くときに村資金から払い、ランの所持金になる（利息なし） | あり。ただし ＋ を 1 回押すだけ（APC 776-780。`AddCarry(carryMax)` なので 1 回で上限になる） | なし | 質問なし（いつも上限まで） | `ShouldCarryMaxFunds` は既定で true | 割合で決めるなら `carrySlider` か `OnCarrySliderChanged` | なし |
| C2 | 持ち込み資金をスライダーで決める | `carrySlider`（:31）→ `SetCarry` | 上限が 0 のときは使えない | 同上 | 同上 | なし | なし | 0 | ＋ で足りている | 同上 | なし |
| C3 | 難易度（遊ぶ日数）を選ぶ | `easyButton`/`normalButton`/`hardButton`（:40, 42, 44）→ `OnDifficultySelected` → `PreparationModel.SelectDifficulty` | 村の格: かんたん=いつでも、ふつう=村、むずかしい=町。一度クリアした長さは格が下がっても選べる（Village/VillageRank.cs:122-138） | かんたん 配信2回・6〜8日 / ふつう 4回・12〜20日 / むずかしい 6回・24〜36日 | ランの長さ。村資金に換える倍率 ×1/×1.25/×1.5 | なし（初期値のまま） | なし | 0 | 押していない | `OnDifficultySelected.OnNext(GameModeId)`。解禁されていないボタンは知らせが出るだけ | なし |
| C4 | スターターレリックを選ぶ・外す | `PreparationChoiceSlot.selectButton`（Preparation/PreparationChoiceSlot.cs:17）→ `SelectStarterRelic`（同じものをもう一度押すと外れる） | 普通はいつでも。レアはギルド Lv1 から。エピックはギルド Lv4 から 2 つ | 無料・1 つだけ | ランの最初からレリックを持つ | あり（APC 782-791。いつも先頭） | なし | 質問なし | 既定は先頭 | 一覧は `view.RelicCatalogParent` の子 | なし |
| C5 | スタートダッシュ: 宣伝ビラ | `flyerButton`（:61）→ `ToggleFlyer` | いつでも（切り替え） | 300,000G（asset。コードの既定は 100,000） | 注目 +20、フォロワー +100（GameLifecycleHandler.cs:243-249） | なし | なし | 0 | 押していない | `OnFlyerToggled`。払うのは出店のとき | なし |
| C6 | スタートダッシュ: 目利きの手引き | `appraisalButton`（:64） | 同上 | 500,000G（既定 150,000） | 全銘柄の需要 +0.15 | なし | なし | 0 | 同上 | `OnAppraisalToggled` | なし |
| C7 | スタートダッシュ: 初回減免状 | `graceButton`（:67） | 同上 | 700,000G（既定 200,000） | 最初の納付が 30% 減る | なし | なし | 0 | 同上 | `OnGraceToggled` | なし |
| C8 | レリック一覧を送る | `relicPrevButton` / `relicNextButton`（:57-58） | – | – | 見るだけ | なし | なし | 0 | – | – | なし |
| C9 | 店を開く（出店） | `departButton`（:76）→ `PreparationPresenter` 172-176 → `Depart`（private 267-301） | チュートリアル中は受け付けない | 持ち込み＋スタートダッシュが村資金を超えると断られる | 店のシーンへ | あり（APC 793-800） | なし | 質問なし | – | – | なし |
| C10 | 村へ戻る | `backButton`（:77）→ 176-180 | 同上 | – | 村へ | なし | なし | 0 | – | – | なし |

### D. 店の一日: 画面の切り替えとフェーズ

店ホームは「イベント → 仕入れ → 陳列 → 営業」の順にフェーズが進む（GameFlow/TurnPhaseManager.cs）。ボタンはフェーズごとのまとまりで出し入れされる（TomsShop/TurnPhaseView.cs:88-98、置き場所はプレハブ `Assets/Prefabs/Screens/TomsShop.prefab`）。

- **仕入れのとき**: 勇者・新聞・地図・鍛冶屋・情報屋
- **陳列のとき**: 占い師・陳列・納付・魔王軍支援・店・広告
- **営業のとき**: 営業開始

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| D1 | 次のフェーズへ | `TurnPhaseView.advanceButton`（:20）→ `TurnPhasePresenter` 25-27 → `AdvanceTurnPhase` | 仕入れ・陳列のとき。チュートリアルのロック中は押せない | – | 仕入れ→陳列→営業 | あり（DAP `Advance` 693-697。`OnAdvanceClicked`） | なし | 質問なし | – | – | なし（ヘッドレスはフェーズなし） |
| D2 | 前のフェーズへ | `backButton`（:22。シーンでつないでいる） | 陳列・営業のとき | – | 1 つ戻る | なし | なし | 0 | 戻る必要がない | – | なし |
| D3 | 営業開始 | `TomsShopView.StartShopButton`（TomsShop/TomsShopView.cs:15）→ `TomsShopPresenter.RequestStartSales` 635 → `StartSales` 733 | 営業のとき | – | 陳列した品が客の数まで売れ、売り注文になる。入金は翌日の価格で（TurnEndSummaryPresenter.cs:85-142） | あり（DAP 674-689。`OnStartShopClicked`） | なし | 質問なし | – | – | あり（`StartSales`） |
| D4 | 「品出しゼロで営業します」に答える | ポップアップ（TomsShopPresenter.cs:655-674）。「営業する」/「陳列にもどる」 | 何も陳列していないとき | – | – | あり（いつも「営業する」。DAP 503-510） | なし | 質問なし | – | – | なし |
| D5 | 「明日は配信です」に答える | ポップアップ（TomsShopPresenter.cs:680-717）。「このまま営業する」/「勇者を見る」 | 配信の前日に 1 回。第1ランは占い師に会ってから。最後は「明日は最後の配信です」 | – | 「勇者を見る」で勇者画面へ（F の装備の入口） | あり（いつも「このまま営業する」。DAP 511-518） | なし | 0（勇者を見るほう） | 装備の手が無いので営業を選ぶ | 勇者を見せるなら、ここで `OnCancel` か店の `HeroButton` | なし |
| D6 | 売上のまとめ演出を飛ばす | 何かの入力（TomsShop/SalesDigestView.cs:52, 206-207） | 演出中 | 最長 10 秒 | 演出だけ | なし（自動プレイ中は演出なし。TomsShopPresenter.cs:762） | なし | 質問なし | – | – | なし |
| D7 | 営業サマリーを確かめて次の日へ | `TurnEndSummaryView.confirmButton`（:59）→ `TurnEndSummaryPresenter` 66-67 → `NextTurn` | サマリー画面 | – | 日が進む | あり（DAP `StepSummary` 794-827） | なし | 質問なし | – | – | あり（`EndDay`） |
| D8 | 行動のヒントを押す | `TurnActionHintItemUI.actionButton`（TomsShop/TurnActionHintItemUI.cs:13）→ `TurnActionHintPresenter.Navigate` 80-99 | 今のフェーズのヒントだけ（シーンでつながっているか未確認） | – | 陳列・鍛冶屋・広告を開く | なし | なし | 0 | 近道なだけ | – | なし |
| D9 | 道具屋を開く | `ToolButton`（TomsShopView.cs:14） | **押せない**。いつも隠れている（383-385） | – | – | なし | なし | 0 | 使われていない機能 | 足さない | なし |

### E. 仕入れ（鍛冶屋）

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| E1 | 鍛冶屋を開く | `TomsShopView.BlaskSmithButton`（:10。綴りは Blask のまま）→ `TomsShopPresenter` 159-161 | 仕入れのとき・いつでも | – | 鍛冶屋の画面 | あり（DAP 699-706。`OnBlacksmithClicked`） | なし | 質問なし | – | – | – |
| E2 | お任せ購入（方針と予算を決めて買う） | `BlackSmithView.autoBuyButton`（BlackSmith/BlackSmithView.cs:30）→ `AutoBuyBudgetPopup`（`budgetInputField`・`strategyDropdown`・`fullAmountButton`・`halfAmountButton`・`confirmButton`、AutoBuyBudgetPopup.cs:13-20）→ `BlackSmithPresenter.HandleAutoBuy` 426-439 → `ItemModel.AutoPurchase` 714-768 | いつでも。初回チュートリアルは予算が全額で固定 | 予算は 0〜所持金。方針は 4 つ（注4） | 方針の順に、在庫の上限まで買う | あり（APC `RunAutoBuyPopupAsync` 153-194。うまくいかなければ DAP 782） | budget / buy_style | budget 1,422 回、buy_style 1,422 回（割安 550 / おすすめ 537 / 次ダンジョン特化 174 / 配当 161） | – | 済み。特化は弱点が分かっているときだけ効く | あり（`AutoBuy`） |
| E3 | 1 銘柄を選んで数を決めて買う | 行をクリック（ItemShopSlot.cs:342）→ `ItemDetailPanel.quantitySlider` / `plusButton` / `minusButton` / `purchaseButton`（ItemDetailPanel.cs:38-43）→ `BlackSmithPresenter.HandlePurchase` 441-463 | チュートリアルのロック中は買えない | 1 個の値段＝今の価格×レリックの割引（最低 1G）。数の上限＝min(在庫の空き, 所持金÷1 個の値段) | 在庫が増える。平均の買値を記録する | 一部（配信前の弱点武器だけ。APC `BuyItemManuallyAsync` 197-244） | focus_share / weapon | focus_share 217 回、weapon 217 回 | ふだんの日の個別購入はない | 注5 | あり（`Buy`） |
| E4 | 武器 / 防具タブ | `weaponButton` / `armorButton`（View:24-25）→ Presenter 363-376 | ロック中は不可 | – | 一覧を切り替える | あり（E3 の中で） | なし | 質問なし | – | – | – |
| E5 | 並べ替え | `sortDropdown`（View:60）→ Presenter 393-418 | 武器・防具タブのとき | – | おすすめ（期待収益）/ 需要 / 価格。見た目だけ | なし | なし | 0 | 見た目だけ | – | なし |
| E6 | 値動きと説明を見る | 行を選ぶ・`infoButton`（ItemShopSlot.cs:71） | – | 最大 12 点 | 見るだけ（期間の切り替えはない） | 一部（E3 で行を選ぶとき） | なし | 質問なし | – | – | なし |
| E7 | 鍛冶屋と話す | `characterButton`（View:36） | – | – | セリフだけ | なし | なし | 0 | – | – | なし |
| E8 | 付せんから新聞を開く | `ScrapStickyNotesUI.newspaperButton`・付せん（BlackSmith/ScrapStickyNotesUI.cs:15, 27, 39）→ `BlackSmithPresenter.OpenNewspaper` 207-211 | ロック中は不可 | – | 新聞（I 節）。閉じると鍛冶屋へ戻る | なし | なし | 0 | 店ホームの新聞ボタンを使う | – | なし |
| E9 | 特殊武器タブ | `specialWeaponButton`（View:27） | **押しても何も起きない**（Presenter 382 は取引所が情報屋へ移ったと書いてある。シーンで隠しているかは未確認） | – | – | なし | なし | 0 | 使われていない機能 | 足さない | なし |
| E10 | 鍛冶屋 Lv を上げる | 開発タブ `developButton`（View:26）→ `levelUpButton`（View:66）→ `HandleBlackSmithLevelUp` 804-822 → `TomsModel.UpgradeBlacksmith` 320-335 | 開発タブで、お金が足りるとき | Lv1→2 3,000 / 2→3 6,000 / 3→4 12,000 / 4→5 20,000G。最大 Lv5。職人組合 Lv2〜3 で −15%、Lv4〜5 で −25% | 鍛冶屋 Lv 以下の武具が一覧・お任せ仕入れ・勇者の装備に加わる | なし | なし | **0** | 押していない（優先度 2） | `developButton` → `levelUpButton`、または `BlackSmithView.OnLevelUpRequested.OnNext`。モデルは `TomsModel.UpgradeBlacksmith()`（bool を返す） | あり（`UpgradeBlacksmith`） |
| E11 | 鍛冶屋を閉じる | `closeButton`（View:23）→ Presenter 297-313 | ロック中は不可 | – | 配信日は「配信を始める / 仕入れを続ける」が出る（L3） | あり（DAP 791。`OnCloseRequested`） | なし | 質問なし | – | – | – |

### F. 勇者

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| F0 | 勇者画面を開く | `TomsShopView.HeroButton`（:11）→ `TomsShopPresenter` 163-165。または D5 の「勇者を見る」 | 第1ランは占い師に会ってから（3日目ごろ）。仕入れのとき。**配信日は開けない**（「配信の日は装備を変えられません」TomsShopPresenter.cs:700） | – | 勇者画面 | なし | なし | 0 | 開かない | 店ホームの `HeroButton` を押す。閉じるのは `HeroPanelView.closeButton`（HeroPanelView.cs:12） | – |
| F1 | 武器を装備する | `HeroPanelView.weaponDropdown`（Hero/HeroPanelView.cs:14）→ `OnWeaponChanged(weaponIds[i])` → `HeroPanelPresenter.SetWeapon` 156-168 → `SaveEquipment` 200-213 | 鍛冶屋 Lv 以下の武器。0 番は「なし」 | 無料。**在庫がなくてもよく、品も減らない** | 攻撃 +10/18/26/34/42%（ランク 1〜5）。武器の属性が敵の弱点なら、その敵への攻撃 ×2（戦いだけ。クリア見込みには入れない）。勇者が勝つと、その品の値段が上がる | なし | なし | **0** | 勇者画面を開かない（優先度 1） | 注6 | あり（`EquipHero`） |
| F2 | 防具を装備する | `armorDropdown`（:15）→ `armorIds` → `SetArmor` 170-182 | 同上（防具） | 無料 | 防御 +15/25/35/45/55%、HP +5〜25% | なし | なし | **0** | 同上 | 注6。防具だけを装備すると、配信側が防具を武器として読む恐れがある（注7） | あり（`EquipHero`） |
| F3 | 作戦を選ぶ | `tacticsDropdown`（:16）→ `tacticsValues[i]` → `SetTactics` 184-190 | いつでも | 無料 | 配信中の値動きと配信熱の倍率だけ（注8）。クリア見込みには効かない | なし | なし | **0** | 同上 | `tacticsDropdown.value = index`（index は enum の値） | なし |
| F4 | 細工: 業物 / なまくら | `craftFineButton` / `craftDullButton`（:42-43）→ `OnCraftRequested` → `HeroPanelPresenter.Craft` 266-276 → `ArtisanCraft.Apply`（Village/ArtisanCraft.cs:56-70） | 職人組合 Lv5。武器を装備していて、次の配信がある。配信と配信の間に 1 回 | 無料 | 業物＝次の配信だけ、武器の属性がダンジョンの弱点になる。なまくら＝次の配信だけ攻撃 ×0.5 | なし | なし | **0** | 同上（解禁も遅い） | ボタンを押す。弱点を知らないと業物は意味が薄い（弱点が無ければ効かない :87） | なし |
| F5 | 勇者と話す・説明を見る | `characterButton`（:13）・各 info ボタン（:34-37） | – | – | セリフ・説明だけ | なし | なし | 0 | – | – | なし |

### G. 情報屋・取引所

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| G1 | 情報屋を開く | `TomsShopView.InfoButton`（:13）→ `TomsShopPresenter` 168-170 | 第1ランは占い師に会ってから。仕入れのとき | – | 情報屋（地図・取引所・開発のタブ） | あり（G3 のときだけ） | なし | 質問なし | – | – | – |
| G2 | タブを切り替える | `InfoBrokerView.mapButton` / `exchangeButton` / `developmentButton`（Information/InfoBrokerView.cs:21, 42, 31） | – | – | パネルを切り替える | 一部（地図タブだけ。APC 494-499） | なし | 質問なし | – | – | – |
| G3 | ダンジョンの弱点情報を買う | 行 `MapPurchaseSlot.infoButton`（MapPurchaseSlot.cs:16）で選ぶ → `MapInfoView.purchaseButton`（MapInfoView.cs:33）→ `InfoBrokerPresenter.PurchaseDungeonInfo` 132-172 | まだ買っていないダンジョン。魔王城は取り扱いがない | 配信 0 回目 2,000 / 1 回目 5,000 / 2 回目から 10,000G（DungeonInfoCosts.csv。日数ではなく配信の回数で決まる） | 弱点が分かる。地図・支援・鍛冶屋の帯・占い・品出しのオート選択が弱点を使うようになる | あり（APC `BuyDungeonIntelAsync` 482-523。配信の 2 日前まで） | intel | 410 回（買う 256 / 買わない 154） | – | Presenter は買ったかどうかを確かめない。直接 `OnMapPurchaseClicked` を呼ぶと二重に払う | なし |
| G4 | 情報屋 Lv を上げる | 開発タブ → `InfoBrokerView.levelUpButton`（:37）→ `InfoBrokerPresenter.HandleLevelUp` 216-229 → `TomsModel.UpgradeInfoBroker` 341 | お金が足りるとき | Lv1→2 2,500 / 2→3 6,000 / 3→4 12,000 / 4→5 20,000G。最大 Lv5。酒場 Lv1 で Lv2 から、Lv4 で Lv3 から始まる | Lv2 で長期債と属性の投資信託 5 本が解禁。債券の口数の上限＝10×Lv。Lv3 から先は今の商品では解禁が増えない | なし | なし | **0** | 押していない（優先度 2） | `developmentButton` → `levelUpButton`、または `OnLevelUpRequested.OnNext` | なし |
| G5 | 金融商品を選んで数を決める | `ItemShopSlot` 行 → `ExchangePanelController.SelectProduct` 153。`FinanceDetailPanel.plusButton`/`minusButton`/`quantitySlider`（FinanceDetailPanel.cs:28-31） | 未解禁の商品は知らせだけ | 1〜max(買える数, 持っている数) | – | なし | なし | 0 | 開かない | – | – |
| G6 | 債券を買う | `FinanceDetailPanel.buyButton`（:31）→ `ExchangePanelController.HandleBuy` 188 → `PortfolioModel.BuyBond` 109 | 短期債 Lv1、長期債 Lv2 | 短期 2,000G/口・6%・5日、長期 5,000G/口・18%・12日。手数料なし。口数の上限 10×情報屋 Lv | 満期の朝に元本と利息が自動で入り、朝レポートに出る。**途中で売れない**（売るボタンは出ない） | なし | なし | **0** | 押していない（優先度 4） | 情報屋 → `exchangeButton` → 行 → `buyButton`。お金が満期まで動かないので納付に注意 | あり（`BuyBond`） |
| G7 | 投資信託を買う | 同じ `buyButton` → `PortfolioModel.BuyFund` 138 | 市場 Lv1、属性 5 本（火・水・木・光・闇）は Lv2 | 1 口＝1,000×（全銘柄の 今の価格÷基準価格 の平均。属性で絞る）。買う手数料 2%（酒場 Lv3 で −1pt、Lv4 で −2pt）。口数の上限なし | 口数を持つ。配当はない | なし | なし | **0** | 同上 | 同上 | なし |
| G8 | 投資信託を売る | `FinanceDetailPanel.sellButton`（:32）→ `HandleSell` 215 → `PortfolioModel.SellFund` 168 | 持っているとき | 売る手数料 2%。古い口から売る | 今の価格でお金に戻す | なし | なし | **0** | 同上 | 納付が足りないときは強制で売られる（K3） | なし |
| G9 | 情報屋と話す・閉じる | `characterButton`（:13）/ `closeButton`（:12） | – | – | – | 閉じるのはあり（APC 521） | なし | 質問なし | – | – | – |

### H. 地図・魔王軍支援・占い師

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| H1 | 地図を見る | `TomsShopView.MapButton`（:16）→ `MapView` のダンジョンボタン（Map/MapView.cs:10-15）→ `DungeonInfoView`。戻るのは `backButton`（:32） | 第1ランは占い師に会ってから。仕入れのとき | 無料 | 見るだけ。配信の予定表は領主館 Lv5 のときだけ出る（MapPresenter.cs:54-74） | あり（APC `PeekMapAsync` 420-432。開いて眺めて戻るだけ。人格はめたんが偶数の日） | なし | 質問なし | 見るだけ | – | なし |
| H2 | 魔王軍支援を開く | `TomsShopView.DungeonLevelUpButton`（:17）→ `TomsShopPresenter` 193-195 | 最初の配信のあと。陳列のとき | – | – | あり（H3 のとき） | なし | 質問なし | – | – | – |
| H3 | 魔王軍を支援する（ダンジョンの Lv を上げる） | 行 `DungeonLevelUpSlot.selectButton` → `levelUpButton`（Map/DungeonLevelUpSlot.cs:12-13）→ `DungeonLevelUpPresenter.HandleLevelUp` 132-162 | お金が足りるとき。最大 Lv5。Lv0 から始まる | Lv0→1 10,000 / 1→2 10,000 / 2→3 20,000 / 3→4 40,000 / 4→5 150,000G | 敵が強くなり、クリア見込みが下がる。勇者が負けたときの防衛報酬 Lv1 15,000〜Lv5 120,000G | あり（APC `SupportDemonArmyAsync` 435-480。所持金の 3 割を残す） | support | 415 回（0 段 114 / 1 段 127 / 2 段 59 / 上限まで 115） | – | モデルのメソッドはない（`DungeonData` を直接書きかえる） | あり（`SupportDungeon`） |
| H4 | 支援の画面を閉じる | `DungeonLevelUpView.closeButton`（:28） | – | – | – | あり（APC 477） | なし | 質問なし | – | – | – |
| H5 | 占い師を開く・無料の情報を見る | `TomsShopView.ProphetButton`（:19）→ `ProphetPresenter` 72-83 | 第1ランは占い師に会ってから。陳列のとき | 無料 | 値動きの大きい品・高い品・次のダンジョン・おすすめ | なし | なし | 0 | 開かない | – | – |
| H6 | 占ってもらう | `ProphetView.fortuneButton`（Prophet/ProphetView.cs:17）→ `ProphetPresenter.BuyFortune` 106-122 | 1 日 1 回。買うとボタンが消える | 15,000G。酒場 Lv4 からは 10,000G | この先 3 日で上がる品を最大 3 つ、下がる品を合わせて 4 行。1 行ごとに 8 割当たる | なし | なし | **0** | 押していない（優先度 5） | `fortuneButton` か `OnFortuneClicked.OnNext` | あり（`BuyFortune`） |

### I. 新聞

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| I1 | 新聞を開いて読む | `TomsShopView.NewspaperButton`（:23）→ `TomsShopPresenter` 218-224 → `NewspaperPresenter.Entry` 105-130 | 第1ランは 2 日目か、占い師に会ってから。仕入れのとき。**自分で開くまで出ない** | 無料 | 記事で需要の動きを読む | あり（APC `ReadNewspaperAsync` 399-417。開く → 次の面 → 閉じる） | newspaper | 1,422 回（読む 830 / 読まない 592） | – | 中身（どの記事か）は人格に渡していない | なし（ヘッドレスは紙面を全部見る） |
| I2 | 面を送る・タブ（一面/二面/市況/うわさ/自店） | `NewspaperView.prevPageButton` / `nextPageButton`（Newspaper/NewspaperView.cs:134-135）、`pageTabs`（:133） | 記事のある面だけ | 無料 | 自店の面は朝レポートの中身を出す | 一部（次の面を 1 回だけ） | なし | 質問なし | – | – | – |
| I3 | 記事を一面に出して読む | 記事のマス `NewsArticleCellUI.rootButton`（:20）→ `NewspaperPresenter.OpenArticle` 574-592 | – | 無料 | 本文が見える。既読になる | なし | なし | 0 | – | スクラップと裏取りのボタンは一面の記事にだけある | – |
| I4 | 壁新聞（無料）を読む | `wallPaperButton`（:112） | いつでも | 無料 | タブロイド紙の一面 1 本（社名なし）と埋め草 2 本まで | なし（購読がないと最初はこれが出る） | なし | 質問なし | – | – | – |
| I5 | 新聞社を購読する | 社のカード `NewspaperCompanyCardUI.subscribeButton`（UI/NewspaperCompanyCardUI.cs:41）→ `NewspaperPresenter.Subscribe` 628-647 | 枠: 店 Lv1〜2 で 1 社、Lv3〜4 で 2 社、Lv5 で 3 社。お金が足りること | 王国 9,000 / ギルド 8,000 / 商工 6,000 / タブロイド 3,000 / 職人 4,000G（5 日契約。酒場 Lv2〜 で 25〜65% 引き）。**解約はない** | その社の紙面が読める（経済はどの社も同じに動く） | なし | なし | **0** | 押していない（優先度 5） | カードの `subscribeButton`。空いた枠はいつでも埋められる | あり（`SubscribeNewspaper`） |
| I6 | 購読した社に切り替える | カードの `rootButton`（:43） | 購読中 | 無料 | – | なし | なし | 0 | 購読しない | – | – |
| I7 | 過去の号を見る | `prevIssueButton` / `nextIssueButton`（:89-90）→ `StepIssue` 684-693 | 3 日前まで。その日に購読していた社だけ（壁新聞はいつでも） | 無料 | – | なし | なし | **0** | – | – | なし |
| I8 | 閉じる | `closeButton`（:91） | – | – | 元の画面へ | あり（APC 414） | なし | 質問なし | – | – | – |
| I9 | 記事をスクラップする | 一面の記事 `NewsLeadArticleUI.scrapButton`（UI/NewsLeadArticleUI.cs:22）→ `NewspaperPresenter.PinArticle` 594-602 → `ScrapbookModel.Pin` | 報道記事だけ。枠 3 つ（増えない）。はがせない | 無料 | 鍛冶屋の付せんに出る | なし | なし | **0** | 押していない（優先度 5） | 先に I3 で一面に出す | あり（`PinScrap`） |
| I10 | スクラップの全文を見る | カード `NewsScrapCardUI.rootButton`（:28） | – | 無料 | 見るだけ | なし | なし | 0 | – | – | – |
| I11 | 予想メモを付ける | `popupPredictionDropdown`（NewspaperView.cs:170）→ `SetPrediction` 697-702 | スクラップした記事 | 無料・17 択 | 表示だけ（当たり外れが出る） | なし | なし | **0** | – | – | なし |
| I12 | スクラップを 1 件確かめる | カードの `confirmButton`（NewsScrapCardUI.cs:25）→ `ConfirmOne` 704-714 | 効く日を過ぎた未確認のもの | 1,500G | ○本当 / △大げさ / ×うそ が分かる。枠が空き、その社の正確さが記録される | なし | なし | **0** | 押していない | – | あり（`ConfirmScrap`） |
| I13 | 3 件まとめて確かめる | `batchConfirmButton`（NewspaperView.cs:143）→ `ConfirmBatch` 717-736 | 未確認が 3 件 | 4,000G | 同上 ×3 | なし | なし | **0** | 同上 | – | あり（`ConfirmScrapBatch`） |
| I14 | 裏を取る | 一面の記事の `vetButton`（NewsLeadArticleUI.cs:24）→ `VetArticle` 767-777 | 酒場 Lv5。配信と配信の間に 1 回 | 無料 | 記事に ○△× の印が付く | なし | なし | **0** | 同上 | – | なし |

### J. 店 Lv・設備・広告・陳列

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| J1 | 店の画面を開く | `TomsShopView.ShopUpgradeButton`（:20）→ `TomsShopPresenter` 208-210 | 第1ランは最初の納付のあとか、最初の配信の 2 日あと。陳列のとき | – | 店 Lv と設備のタブ | なし | なし | 0 | 開かない | – | – |
| J2 | 店 Lv を上げる | `ShopUpgradeView.upgradeButton`（TomsShop/ShopUpgrade/ShopUpgradeView.cs:23）→ `ShopUpgradePresenter.HandleUpgrade` 64 → `TomsModel.UpgradeShop` 362-378 | 最大でなく、お金が足りるとき | Lv2 4,000 / Lv3 10,000 / Lv4 22,000 / Lv5 45,000G（ShopLevelSettings.asset） | 陳列できる種類 3/5/7/9/12、設備の枠 1/2/3/4/6。新聞の購読の枠も増える | なし | なし | **0** | 押していない（優先度 3） | `ShopUpgradeButton` → `upgradeButton`、または `OnUpgradeClicked.OnNext` | あり（`UpgradeShop`） |
| J3 | 店 Lv ⇄ 設備のタブ | `ShopFacilityTabsView.upgradeTab` / `machineTab`（ShopFacilityTabsView.cs:18-19） | – | – | – | なし | なし | 0 | – | 設備の画面へはタブからしか入れない（`MachineShopButton` は出ない） | – |
| J4 | 設備を買って置く | 行 `ShopMachineSlotUI.selectButton`（:17）→ `ShopMachineView.purchaseButton`（:27）→ `ShopMachinePresenter.HandlePurchase` 254 → `ShopMachineModel.TryPurchaseAndPlace` 82 | 店 Lv が設備の必要 Lv 以上。枠に空きがある。お金が足りる | 9 種 5,000〜18,000G（注9）。職人組合で値引き、枠も増える。同じ設備をいくつも置ける | 次の朝から効く | なし | なし | **0** | 押していない（優先度 3） | 設備の画面 → 行 → `purchaseButton` | あり（`PlaceMachine`） |
| J5 | 置いた設備を選ぶ | 置いた一覧の行（ShopMachinePresenter.cs:182-188） | – | – | J6・J7 の相手を決める | なし | なし | 0 | – | – | – |
| J6 | 設備を外す | `removeButton`（ShopMachineView.cs:28）→ `HandleRemove` 284 | 置いているとき | 買値の 50% が戻る | 枠が空く | なし | なし | **0** | – | – | なし |
| J7 | 作る品を変える（自動鍛造炉） | `producedItemDropdown`（:32）→ `HandleProducedItemChanged` 239 | 自動鍛造炉と大型魔導炉だけ。候補は鍛冶屋 Lv 以下 | – | 次の朝から、1 日 1,000G / 2,500G ぶん作る | なし | なし | **0** | – | – | なし |
| J8 | 広告を選ぶ（1 回目のタップ） | `AdvertisementSlotUI.slotSelectButton`（AdvertisementSlotUI.cs:22）→ `AdvertisementPresenter.OnSlotTapped` 179 | 最初の配信のあと。陳列のとき（`AdvertisementButton` :18） | – | どれだけ上がるかの見込みを出す | なし | なし | 0 | – | – | – |
| J9 | 広告を打つ（同じものをもう一度タップ） | 同じボタン → `ExecutePurchase` 202 → `MarketingFacade.ExecuteAdvertisement` 141 | お金が足りるとき。回数の制限はない | 10万〜30万G（注10）。フォロワー割引・バズ割引あり | 信頼・注目・拡散・顧客維持・フォロワーが上がる（各 1,000 まで） | なし | なし | **0** | 押していない（優先度 5） | 2 回押す。値段が高いので納付に注意 | あり（`BuyAdvertisement`） |
| J10 | 広告のステータス説明を見る・閉じる | `trustInfoButton` 〜 `followerInfoButton`（AdvertisementView.cs:77-89）、`closeButton`（:70） | – | – | 見るだけ | なし | なし | 0 | – | – | – |
| J11 | 陳列: オート陳列 | `SetItemButton`（TomsShopView.cs:12）→ `ItemSelectionView.autoDisplayButton`（:17）→ `ItemSelectionPresenter` 86-94 → `ItemModel.AutoSetDisplay` 791 | 陳列のとき | 陳列できる種類の上限まで（店 Lv＋レリック） | 期待収益の高い順に、在庫を全部並べる | あり（DAP `DoAutoDisplay` 722-746） | なし | 質問なし | – | – | あり（`SetDisplay`） |
| J12 | 陳列: 1 銘柄を出す・引っこめる | `ItemSelectionSlot.selectButton`（ItemSelectionSlot.cs:12）→ Presenter 125-138 | 種類の上限まで | – | – | なし | なし | 0 | オートで足りる | – | あり（`SetDisplay`） |
| J13 | 陳列: 並べる数 | `quantitySlider`・`pulusButton`（綴りは pulus のまま）・`minusButton`（:13, 16, 17） | – | 0〜在庫 | 並べなかった分は在庫に残る | なし | なし | 0 | 同上 | – | あり |
| J14 | 陳列から外す | `ItemDisplaySlot.removeButton`（:10） | – | – | – | なし | なし | 0 | 同上 | – | あり（`ClearDisplay`） |
| J15 | 陳列: 武器・防具タブ / 説明 / 閉じる | `weaponPanelButton`・`armorPanelButton`・`infoButton`・`closeButton`（ItemSelectionView.cs:12-18） | – | – | 閉じると保存する | 閉じるのはあり（DAP 613） | なし | 質問なし | – | – | – |

### K. 納付・融資・レリック・イベント・朝レポート

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| K1 | 決まった日の納付（強制） | 朝に自動で出る（TomsShopPresenter.cs:414-421）→ `DebtView.payButton`（Debt/DebtView.cs:16）→ `DebtPresenter.OnPay` 167-188 | 7 日ごと | 15,000×1.5^(回数−1)（asset）。足りないときの順: 今日の売り注文を前倒し → 投資信託・債券を強制で売る → 在庫を 6 割で投げ売り | 払うとレリック3択が来る | あり（DAP `HandleDebtPanel` 831-863） | なし | 質問なし | 選べない | – | あり（自動で処理） |
| K2 | 自分から先に納付する | `TomsShopView.debtPaymentButton`（:41）→ `DebtPresenter.ShowVoluntary` 90 → `payButton` / `closeButton`（:17） | いつでも（陳列のとき）。お金が足りるとき | 次の回の金額 | 回数が 1 つ進むので、次の強制の納付が来ない。レリック3択が来る | なし（パネルが開けば閉じる） | なし | **0** | 押していない（優先度 5） | `debtPaymentButton` → `payButton`。早く払うと、そのぶん現金が減る | なし |
| K3 | 破産して結果へ | `DebtView.goToResultButton`（:22）→ `OnBankruptcy` 190 | 売り払っても足りないとき | – | ゲームオーバーへ | あり（APC 441-445） | なし | 質問なし | – | – | あり |
| K4 | 勝負の融資の申し出に答える | ポップアップ（`DebtPresenter.TryOfferBankLoan` 207-250）。「借りる」/「今回は使わない」 | 銀行 Lv5。ランで 1 回。配信の前日 | 700,000G を借りて 770,000G を返す | すぐに +70万G。配信の翌朝に強制で返す | あり（いつも「使わない」。DAP 479-485） | なし | **0** | 自動操作が断る | `PopUpData.OnConfirm`。返せないと破産が近い | なし |
| K5 | 融資を返す | 返す日の朝（TomsShopPresenter.cs:424-429）→ `payButton` → `OnPayBankLoan` 272 | 返す日 | 770,000G（足りなければ K1 と同じように売る） | – | あり（K1 と同じ経路） | なし | 質問なし | – | – | なし |
| K6 | レリック3択から 1 つ取る | `TomsShopView.relicChoiceButtons[i]`（:50）→ `TomsShopPresenter` 227-238 → `RelicRewardService.ChoosePending` 123 | 配信で勝ったとき・魔王側に賭けて当たったとき・納付のたび | 3 択（ギルド Lv3 で 4 択）。持てる数の上限なし | レリックを持つ | あり（DAP `HandleRelicChoice` 893-912。`OnRelicChoiceSelected`） | なし | 質問なし（ずんだもんは乱数、めたんは先頭。PersonaStrategy.cs:896-897） | 質問がない | 質問を足すなら、候補の名前と効果を state に入れる | あり（`ChooseRelic`） |
| K7 | レリックを辞退してお金をもらう | `relicChoiceSkipButton`（TomsShopView.cs:53）→ `TomsShopPresenter` 245-259 → `RelicRewardService.DeclineForGold` 143 | K6 と同じ | 500 / 1,200 / 2,500G（いちばん高いレア度で決まる）×ギルドの倍率（1 / 1.5 / 1.5 / 2） | 所持金が増える | なし | なし | **0** | 自動操作が押さない（優先度 5） | `OnRelicChoiceSkipped.OnNext`。パッドの B では押されない | あり（`DeclineRelic`） |
| K8 | 持っているレリックの説明を見る | レリックの帯のアイコン（TomsShopView.cs:297-344） | 1 つ以上持っている | – | 見るだけ | なし | なし | 0 | – | – | – |
| K9 | 朝レポートを閉じる | `morningReportCloseButton`（:46） | 中身があるとき | – | 閉じるとお金の数字が動き出す（TomsShopPresenter.cs:584-592） | あり。ただしパネルを `SetActive(false)` で隠すだけ（DAP 914-923） | なし | 質問なし | – | 閉じるボタンを押す形にすると、数字の演出も合う | なし |
| K10 | 店のイベントを読み進めて決める | `EventView.confirmButton`（TomsShop/TomsShopEvent/EventView.cs:16）→ `OnClickConfirm` 78-103 → `TomsEventExecutor.Execute` | 朝に保留のイベントがあるとき | イベントによる | **選択肢はない**。ページを送り、効果を出して決める | あり（DAP `HandleEventPanel` 865-891） | なし | 質問なし | 選べない | – | あり（`ConfirmPendingEvent`） |

### L. 配信日の朝・品出し

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| L1 | 鍛冶屋へ寄る / このまま配信へ | ポップアップ（GameFlow/PreStreamPresenter.cs:109-115） | 配信日の朝 | – | 寄れば配信の前に仕入れられる | あり（DAP 487-495。人格はいつも寄る） | なし | 質問なし | 既定で true | – | なし |
| L2 | 勝負配信を宣言する（勇者が勝つ / 魔王が勝つ） | ポップアップ 3 枚（PreStreamPresenter.cs:160-206）→ `StreamDeclaration.Declare` 50-58 | 広場 Lv5。ランで 1 回 | 無料 | 当たり: (配信の売上＋防衛報酬)×0.5 を上乗せ、フォロワー +6,000、注目 +100。外れ: フォロワー −2,000、注目 −60 | あり（いつも「今回は使わない」。DAP 479-485） | なし | **0** | 自動操作が断る | `stream_side` の答えをここで使える（ゲームの中で、どちらに付くかをはっきり選べるのはここだけ） | なし |
| L3 | 配信を始める / 仕入れを続ける | 配信日に鍛冶屋を閉じたときのポップアップ（PreStreamPresenter.cs:120-129） | – | – | – | あり（いつも「配信を始める」。DAP 496-502） | なし | 質問なし | – | – | – |
| L4 | 品出し: 1 銘柄を足す | `AvailableItemSlot` をクリック（StreamingSetting/AvailableItemSlot.cs:66-70）→ `StreamingSettingPresenter.HandleSelected` 170 | 在庫があるもの | 8 種まで | 配信に持ちこむ | なし | なし | 0 | オート選択で足りる | `View.OnItemSelected.OnNext(id)` | あり（`StartStream` の items） |
| L5 | 品出し: 数を変える・外す | `SelectedItemSlot.qtySlider` / `plusButton` / `minusButton`（:14-18）、右クリックで外す（:84-88） | – | 1〜在庫 | – | なし | なし | 0 | 同上 | `OnQuantityChanged` / `OnItemDeselected` | あり |
| L6 | 品出し: オート選択 | `StreamingSettingView.autoSelectButton`（:22）→ `HandleAutoSelect` 191-284 | – | 上位 8 種を在庫ぜんぶ | 弱点情報を買っていれば属性で加点する | あり（DAP 1000-1018。オート選択して確定） | なし | 質問なし | – | – | – |
| L7 | 品出しを確定する / 前回と同じで確定 | `confirmButton`（:15）/ `quickConfirmButton`（:18）→ `HandleConfirm` 139 | 前回と同じのボタンは、前回の記録があるときだけ | 何も選んでいなければ「品出しゼロで配信します」 | 配信が始まる | あり（DAP 1012。ゼロの確認には「配信する」。DAP 519-525） | なし | 質問なし | – | – | – |

### M. 配信中

AutoPlay が配信中に何をするかは `AutoPlayController.Streaming.cs` の `StepStreamingAsync` 66-122 にある。1 回に 1 つだけ、次の順に試す。

1. 補充
2. 棚の入れ替え
3. 介入
4. 値動きグラフを見る
5. 介入カードを表に戻す

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| M1 | 一時停止・再開 | `BattleControlView.pauseButton`（Battle/BattleControlView.cs:13）→ `BattleSceneStarter.TogglePause` 562-567 | カウントダウンのあと | – | 戦いと販売が止まる。介入もできない | なし | なし | 0 | 精算を変えないため（AutoPlay_Usage） | – | なし |
| M2 | 倍速（×1 / ×2） | `speedButton`（:20）→ `BattleSpeed.Toggle` | 同上 | – | 進みが速くなる。PlayerPrefs に保存 | なし | なし | 0 | – | 録画の長さを縮めたいときに使える | なし |
| M3 | 配信を途中で終える | `endBattleButton`（:17）→ `ConfirmEndStreamingAsync` 598-627 →「終了する」→ `ForceEndBattle` 629-638 | 補充のポップアップが出ていないとき | – | 結果は中断。売上は残るが、防衛報酬・経験値・レリックはなし。宣言は無効になり、使った回数は戻らない | なし | なし | **0** | 精算を変えるため押さない | 足すなら確認のポップアップにも答える | なし |
| M4 | 介入カードをめくる（勇者側 ⇄ ダンジョン側） | `InterventionCardView.tabButton`（Battle/Intervention/InterventionCardView.cs:30）、横に 60px スワイプ、LB/RB | – | – | 押せるボタンが変わる | あり（APC 305-312, 116-120） | なし | 質問なし | – | – | – |
| M5 | 在庫と売り場を入れ替える（ドラッグ） | `ItemSlotView` 同士のドラッグ（Battle/Scripts/ItemSlotView.cs:213-284）→ `StreamingSalesController.HandleItemSwap` 653-701 | – | 在庫 10 マス（コードのコメント）、売り場のマス数はシーン次第 | **売れるのは売り場の品だけ** | あり（APC `SwapShelfAsync` 283-298。在庫→売り場だけ） | なし | 質問なし | – | リフレクション名 `view`・`itemSlots`・`inventorySlotViews` | なし |
| M6 | 値動きグラフを見る | 在庫のある売り場のマスをクリック → `ItemPriceGraphView.Show`、`closeButton`（ItemPriceGraphView.cs:16） | – | – | 見るだけ | あり（APC `PeekPriceGraphAsync` 377-399。15%） | なし | 質問なし | 見るだけ | 在庫切れのマスを押すと補充になる | なし |
| M7 | 勇者側の介入（回復 緑 / スキル 青 / 必殺 赤） | `healButton`・`skillButton`・`specialButton`（InterventionCardView.cs:41-43）→ `InterventionPresenter.OnHeroRequested` 293 | 勇者側の面 | 回復 10,000 / スキル 50,000 / 必殺 100,000G。必殺は 1 配信 2 回まで（視聴者の赤スパも数える） | 回復 HP+30%、スキル 次の攻撃 ×1.5＋武器の需要 +0.10、必殺 次の攻撃 ×3＋需要 +0.20・価格 ×1.05 | あり（APC `InterveneAsync` 301-359） | stream_side / intervene / intensity | stream_side 364 回（魔王 187 / 勇者 58 / どちらでもない 119）、intervene の方針 706 回、実際に投げたもの 772 回（回復 137 / スキル 63 / 必殺 34 / 待つ 19） | – | 注11 | あり（`StartStream` の interventions） |
| M8 | ダンジョン側の介入（罠 / 呪い / 増援 / ボス強化） | `trapButton`・`curseButton`・`reinforceButton`・`bossBuffButton`（:46-49）→ `OnDungeonRequested` 320 | ダンジョン側の面 | 防衛報酬（Lv）× 罠 0.3 / 呪い 0.5 / 増援 0.8 / ボス強化 1.2（500G 単位、最低 1,000G）。ボス強化は 1 配信 1 回 | 罠 次の被弾に最大 HP×30%（防御を無視）、呪い 防御 ×0.5 を 9 回、増援 魔物 +1、ボス強化 攻撃 ×1.25・被ダメ ×0.8・防御貫通 50% | あり（同上） | 同上 | 投げたもの: 呪い 260 / 罠 161 / 増援 94 / ボス強化 4 | – | 注11 | あり |
| M9 | 介入のルール（共通） | `InterventionPresenter` 201-202, 341 | – | クールダウン 6 秒（共通）。指示は 1 つずつ。払えるのは「使える残高」まで | 勇者側は勇者の手番の頭、ダンジョン側は魔物の手番の頭で効く。終わるまでに効かなかった指示は返金 | – | – | – | – | 押しても受け付けられないことがある（APC 355-357 がログに出す） | – |
| M10 | スキル・必殺で狙う敵を選ぶ | 敵をクリック（Battle/Scripts/CharacterView.cs:172 → `InterventionPresenter.OnCharacterClicked` 347-354）、パッドの Y | – | 無料 | スキル・必殺の狙いだけ | あり（APC 315-328。`view.OnClicked.OnNext`。ボスを先に） | なし | 質問なし | – | – | なし |
| M11 | 在庫切れを補充する | 売り切れで自動で出る / 空いたマスをクリック → `RestockQuantityPopup`（Battle/RestockQuantityPopup.cs）。数は `detailPanel` の `quantitySlider`、買うのは `purchaseButton`、やめるのは `cancelButton` | 途中終了の確認が出ていないとき | 1 個＝その時の配信の値段 × restockPriceRate（既定 0.75）。最大 99。既定 10 | 在庫が増える。売れ残りは終わりに返金。出ている間は戦いが止まる | あり（APC `HandleRestockPopupAsync` 231-280） | なし | 質問なし（ずんだもんは既定の 1.5 倍、めたんは既定。PersonaStrategy.cs:1028-1033） | – | リフレクション名 `restockQuantityPopup`・`panel`・`_maxQuantity`・`_quantity`・`titleText`・`detailPanel`・`cancelButton` | なし |
| M12 | 配信中に設定を開く | 歯車・Start・Esc（Battle/BattleGamepadController.cs:64-68, 112-113, 164-172） | – | – | 一時停止する | なし | なし | 0 | – | – | なし |
| M13 | 最後のバナーを飛ばす | クリック・Enter・Space・パッドの A（BattleSceneStarter.cs:468-476） | 中断のときは出ない | – | 演出だけ | なし（待つ） | なし | 0 | – | – | なし |

### N. 配信の後・ランの終わり

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| N1 | 配信の結果を見て店へ戻る | `BattleResultView.confirmButton`（Battle/BattleResultView.cs:24）。1 回目で演出を飛ばし、2 回目で戻る | – | – | 店へ戻る | あり（DAP 955-972） | なし | 質問なし | – | – | – |
| N2 | 配当通知書を受け取る | `DemonKingPayoutView.receiveButton`（:61）、`skipCatcher`（:63）、パッドの B | 勇者が負け、防衛報酬が 0 より大きい | – | **見るだけ**。お金はもう入っている | あり（DAP 942-953。`ReceiveImmediately`） | なし | 質問なし | 選べない | – | – |
| N3 | ランの結果の演出を飛ばす | `ResultView` の `skipCatcher`（Result/ResultView.cs:48） | 演出中 | – | 演出だけ | なし（待つ） | なし | 0 | – | `ResultView.Skip()` | なし |
| N4 | 村へ帰る（クリア） | `goToTitleButton` / `retryButton`（ResultView.cs:40-41。どちらも同じ）→ `ResultPresenter.FinishRunAndGoVillage` 177-204 | 演出とトコの会話が終わってから | – | 儲けを村資金に換える（注12）→ ランのデータを消す → 村へ | あり（APC `StepResultAsync` 821-843） | なし | 質問なし（結果の一言 `run_result` 126 回） | – | – | なし |
| N5 | 村へ帰る（破産） | `GameOverView.retryButton`（GameOver/GameOverView.cs:12。「村へ戻る」）→ `GameOverPresenter.FinishRunAndGoVillage` 113-153 | 破産の会話が終わってから | – | いちばん儲かっていたとき×0.2 を村へ。持ち込んだ分はなくなる | あり（APC `StepGameOverAsync` 845-868） | なし | 質問なし | – | – | なし |
| N6 | 体験版: ストアページ / タイトルへ | `DemoUpsell.Show`（Demo/DemoUpsell.cs:25-79） | 体験版のビルドだけ | – | Steam のストアを開く / タイトルへ | なし（APC は確認にキャンセルで答える） | なし | 0 | – | – | なし |

### O. 会話（Utage）

| # | 操作 | 画面・入口 | 解禁条件 | 費用・上限 | 効き方 | 自動操作 | 人格の質問 | 使用回数 | 使わない理由 | 足すときの方法と注意 | 旧ヘッドレス |
|---|---|---|---|---|---|---|---|---|---|---|---|
| O1 | 会話を進める | クリック・A・Enter（Scenario/UtageClickForwarder.cs:22-26） | 会話中 | – | 次のページ | あり（APC `HandleScenario` 335-400。`engine.Page.InputSendMessage`） | なし | 質問なし | – | バックログやメッセージ非表示が開いていれば通常表示に戻す（370-377） | なし |
| O2 | Utage の Skip / BackLog / Menu | 会話のプレハブにあるボタン（Utage の機能） | 未確認（親のオブジェクトが出ているかを見ていない） | – | 早送り・ログ | なし | なし | 0 | – | – | なし |
| O3 | 会話の選択肢を選ぶ | `SelectionManager.SelectWithTotalIndex`（APC 381-391、DAP 443-452） | **今のシナリオには選択肢が 1 つもない**（`Assets/TomsScenario/Scenarios/*.csv` に Selection が無い） | – | – | 仕組みはある（ずんだもんは乱数、めたんは先頭） | なし | 0 | 選択肢がない | 選択肢ができたら質問にできる | なし |

### P. 押せない・中身がないもの（まちがえて足さないための記録）

| # | もの | 確かめたこと |
|---|---|---|
| P1 | イベントシーン（EventScene）の確認ボタン | イベントはいつも店の中の小窓で出る（`PendingEventData.UseInlineEventPopup` が true、TomsShopEvent/PendingEventData.cs:15）。ビルドの設定にもシーンが入っていない。APC `StepEventSceneAsync` 870-876 は届かない |
| P2 | 売り注文を出す | 出す画面はない。営業開始（D3）のときに自動で出て、自動で入金される（SellOrderModel は `Place` / `SettleDue` だけ） |
| P3 | 値段を決める | プレイヤーが値段を決める手はない |
| P4 | 闇の店 | `DarkShopManager.OpenDarkShop` は中身が空 |
| P5 | ステマ（配信） | `StreamingView` は使われていない古い UI |
| P6 | 勇者の情報を買う | `HeroInfoView` のボタンは、どこにもつながっていない |
| P7 | 視聴者のスパチャ | プレイヤーは触れない（赤スパは自動で必殺、紫スパは無料の罠） |
| P8 | レリックの引き直し | ない |
| P9 | 新聞の解約 | ない |
| P10 | 債券を途中で売る | 売るボタンが出ない（FinanceDetailPanel.cs:115-116） |

---

## デバッグ専用の操作（製品版には出ない）

- **出る場所**: エディタと開発ビルドだけ（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`、`Debug.isDebugBuild`）。
- **開き方**: F11 で開く（Debug/DebugMenuView.cs:82-85）。GameLifetimeScope.cs:389 のコメントは F12 と書いているが、実際は F11。
- 12 のタブ:
  - **お金**: 所持金 +1千 / +1万 / +10万 / 半分 / 0 / 値を入れる。村資金 ±1万 / 0 / 値を入れる。
  - **レベル**: 鍛冶屋 / 情報屋 / 店 Lv +1・=1、勇者 Lv、ダンジョン Lv ±1、6 施設 ±1（0〜5。施設 +1 で格の知らせも出る）。
  - **レリック**: 乱数で 1 つ、3 択の試し、指定して渡す。
  - **マーケ**: バズ / 大バズ / 炎上を起こす・終える、ステータス ±10、フォロワー。
  - **相場**: ABM の切り替え、経済だけ ×10。
  - **その他**: 次の日へ、全ダンジョンの情報を見せる、全在庫 +10、時間の速さ。
  - **画面移動**: 店のフェーズ・各シーンへ飛ぶ（イベントは使えない）。
  - **オート**: DebugAutoPlayer の開始 / 一時停止 / 停止、一部だけ自動、会話の自動送り。AutoPlay（全シーン通し）の操作盤もここにある。
  - **便利**: +100万、所持金を最大、元本を 0、在庫の道具、イベントを起こす、情報を見せる、保存フォルダを開く、全部保存、FPS、一時停止、勇者の HP を満タン。
  - **ログ**: ログのコンソール。
  - **実績**: 実績のリセット（2 段階で確かめる）。
  - **情報**: 見るだけ。

---

## 注

1. **タイトルのボタン**: シーン（TitleScene.unity:822-823）では、`startButton` と `newGameButton` が同じボタンを指す。1 回押すと「スタート」と「初めから」が続けて届く。APC はこの点をよけて、押せなければ `OnStartRequested` を直接出す（603-615）。
2. **初めからのスロット**: APC は空いたスロットだけを使い、上書きしない（`PickSlot` 622-635）。空きがなければ止まる。セーブのフォルダはすべての worktree で共有なので、確かめる前にバックアップを取る。
3. **村の施設の費用（G）と効果**:

   | 施設 | Lv1 | Lv2 | Lv3 | Lv4 | Lv5 |
   |---|---|---|---|---|---|
   | 領主館 | 800,000 | 2,500,000 | 6,000,000 | 12,000,000 | 20,000,000 |
   | 冒険者ギルド | 500,000 | 1,200,000 | 3,000,000 | 6,000,000 | 10,000,000 |
   | 酒場 | 500,000 | 1,100,000 | 2,800,000 | 5,600,000 | 9,500,000 |
   | 銀行 | 400,000 | 1,000,000 | 2,000,000 | 4,500,000 | 8,000,000 |
   | 職人組合 | 500,000 | 1,200,000 | 2,800,000 | 5,800,000 | 10,000,000 |
   | 広場 | 500,000 | 1,200,000 | 2,800,000 | 5,800,000 | 10,000,000 |

   効果:
   - **領主館**: ほかの施設が上げられる Lv の上限を決める。Lv4 でクリアのボーナス +15%。Lv5 で配信の予定表が出る。
   - **ギルド**: レリックの解禁、辞退でもらえるお金の倍率、Lv3 で 3 択が 4 択になる。Lv5 で呪いのレリックが出る。
   - **酒場**: 始めの情報屋 Lv、新聞の値引き、投資信託の手数料、Lv4 で占いが 10,000G。Lv5 で裏取り。
   - **銀行**: 持ち込みの上限。Lv5 で勝負の融資。
   - **職人組合**: 始めの鍛冶屋 Lv、開発の値引き、設備の枠。Lv5 で細工。
   - **広場**: 始めのフォロワーと注目、配信の売上 +5〜15%。Lv5 で勝負配信の宣言。

   出どころは `Assets/Resources_moved/Village/Village_*.asset` と `Village/VillageEffects.cs`。
4. **お任せ購入の方針**（BlackSmith/AutoBuyStrategy.cs、ItemModel.cs:727-776）:
   - **0 おすすめ順**: 1 日の客の数 ×（適正値 − 今の価格）
   - **1 次ダンジョン特化**: 弱点の属性の品から。弱点を知らないときは期待収益の順
   - **2 割安買い**: 今の価格÷基準価格 が低い順
   - **3 配当重視**: 配当÷価格 が高い順

   対象は、鍛冶屋 Lv 以下のすべての品。
5. **個別購入を足すとき**:
   - リフレクション名は `weaponButton` / `armorButton` / `scrollRect`（BlackSmithView）、`quantitySlider` / `purchaseButton`（ItemDetailPanel、`view.DetailPanel`）。行は `ItemShopSlot.itemId` で探し、`AutoPlayUi.PointerClickAsync` で選ぶ（APC 197-244 と同じ）。
   - 行の中にある `purchaseButton` / `plusButton` / `minusButton` / `quantitySlider`（ItemShopSlot.cs:20-23, 40-41）は、どこにもつながっていない。押しても何も起きない。
6. **勇者の装備を足すとき**:
   - 開き方は二つ。店ホームの `HeroButton`（仕入れのとき）か、D5 で `OnCancel`（勇者を見る）を選ぶ。
   - 武器は `HeroPanelView` の `weaponDropdown.value = i`。i は private `weaponIds`（List<string>）での位置で、0 は「なし」。`onValueChanged` が `OnWeaponChanged` を出す。
   - `suppressEvents` が true の間は通知が出ない。
   - モデルは `HeroModel.heroData.weaponId/armorId/tactics`、`EquipItem`、`ClearEquippedItems`、`SaveHeroData`。
   - 候補の並びは「在庫がある → 弱点に合う → ランク → 価格」（HeroPanelPresenter.cs:362-382）。
   - 配信日は変えられない。前日までに決める。
   - 弱点が分かっていない（情報を買っていない）と、クリア見込みは「情報未購入」と出る。
7. **防具だけを装備したとき（要確認）**:
   - `SaveEquipment`（HeroPanelPresenter.cs:200-213）は、装備した品を [武器, 防具] の順に詰めて `EquippedItemIds` に入れる。武器が空なら防具だけが入る。
   - 配信側は位置で読む。`[0]` を武器、`[1]` を防具とする（BattleSceneStarter.cs:633-634 など）。
   - だから防具だけのときは、防具の ID が武器として読まれるおそれがある。どこまで影響するかは追っていない。
8. **作戦の倍率**（Battle/Scripts/HeroBattleInfluence.cs:5-51）。順に 武器 / 防具 / 属性 / 配信熱。

   | 作戦 | 武器 | 防具 | 属性 | 配信熱 |
   |---|---|---|---|---|
   | 攻撃重視 | 1.20 | 0.90 | 1.05 | 1.15 |
   | 防御重視 | 0.90 | 1.20 | 1.00 | 0.95 |
   | バランス | 1.00 | 1.00 | 1.00 | 1.00 |
   | 慎重 | 0.90 | 1.10 | 0.90 | 0.85 |
   | 属性重視 | 0.95 | 0.95 | 1.20 | 1.05 |

   配信中の値動きの「1 からのずれ」を大きくしたり小さくしたりする。
9. **設備**（Assets/Resources_moved/ShopMachine/*.asset）。値段と、必要な店 Lv:

   | 設備 | 値段 | 店 Lv | 効果 |
   |---|---|---|---|
   | 小さな貯金ゴーレム | 5,600G | 1 | 朝 +800G |
   | 中型貯金ゴーレム | 9,100G | 2 | 朝 +1,300G |
   | 黄金の貯金ゴーレム | 15,400G | 3 | 朝 +2,200G |
   | 鮮度保持ケース | 5,000G | 1 | 需要の下限 +0.08 |
   | 特級鮮度ケース | 12,000G | 3 | 需要の下限 +0.15 |
   | 呼び込みオルガン | 8,000G | 2 | 売上 +10% |
   | 大道芸のステージ | 18,000G | 4 | 売上 +20% |
   | 自動鍛造炉 | 6,000G | 2 | 1 日 1,000G ぶん作る |
   | 大型魔導炉 | 15,000G | 4 | 1 日 2,500G ぶん作る |
10. **広告**（Assets/Resources_moved/Marketing/Ad_*.asset）:

    | 広告 | 値段 | 上がるもの |
    |---|---|---|
    | SNS広告 | 100,000G | 注目 +400、顧客維持 +200 |
    | SNSフォロワーキャンペーン | 120,000G | 注目 +100、フォロワー +10,000 |
    | 口コミキャンペーン | 150,000G | 信頼 +400、拡散 +200 |
    | リピーター施策 | 180,000G | 信頼 +200、顧客維持 +400 |
    | インフルエンサー起用 | 200,000G | 注目 +200、拡散 +400 |
    | 総合マーケティング | 300,000G | 4 つとも +150 |
11. **介入のリフレクション名**:
    - カードは `tabButton`。
    - ボタンは `InterventionButtonWidget` の `healButton` / `skillButton` / `specialButton` / `trapButton` / `curseButton` / `reinforceButton` / `bossBuffButton`。押すのは `.Button`。
    - ボタンの状態は `InterventionPresenter.GetState`、残高は `AvailableBalance`、使った額は `TotalSpent`。
    - 人格は配信の頭で `stream_side` / `intervene` / `intensity` を 1 回聞く。そのあとは重みで投げる（PersonaStrategy.cs:904-981）。「山場」（最初の 1 発・勇者の HP が 3 割を切った・ボスが出た）では、めたんに相談する。
12. **村資金への換え方**（Save/MetaProgressModel.cs:174-190）:
    - **クリア**: max(0, 持ち込み＋min(0, 儲け)) ＋ ボーナス×(1＋領主館の率) ＋ 儲け×難易度の倍率。
    - **ボーナス**: 300,000 ＋ 期日が来た納付の額×2。
    - **破産**: いちばん儲かっていたとき×難易度の倍率×0.2。
    - 換えるときに、プレイヤーが選ぶことはない。

---

## 設計書との食い違い（コードが正しい）

### AutoPlay の設計書

- **AutoPlay_Usage.md 207**: 「村の施設投資は行わない」とある。
  - コードは、実況用の戦略（`IAutoPlayJikkyoExtras`）なら村を見て回って投資する（APC 659-679, 692-725）。人格も投資する（invest 289 回）。
- **AutoPlay_Usage.md 208**: 「新聞・地図などの任意画面は開かない」とある。
  - コードは、実況用の戦略なら新聞・地図を開き、魔王軍支援もする（Procurement.cs:325-341）。
- **AutoPlay_Usage.md 206**: 「朝レポートはパネルを非表示にするだけ」とある。
  - これはコードのとおり。ただし、閉じるボタンを押したときのお金の演出は走らない（注意として書いておく）。
- **Jev_AutoPlay_Design.md 337**: 「新聞の購読制は本体が未実装」とある。
  - 購読は実装済み（`NewspaperPresenter.Subscribe` 628-647）。ヘッドレスにも `SubscribeNewspaper` がある。
- **Jev_AutoPlay_Design.md 338**: 「情報屋・広告・金融商品・マシン・勇者の装備変更はまだボットの手に無い」とある。
  - ヘッドレスには `BuyAdvertisement` / `PlaceMachine` / `BuyBond` / `EquipHero` / `UpgradeShop` / `UpgradeBlacksmith` / `BuyFortune` がある。Jev ボットも `upgrade` / 装備 / 購読 / スクラップを聞いている（AutoPlayJevBot.cs:234-269）。
  - 情報の購入（弱点）だけは、ヘッドレスに無い。

### 出店準備・タイトル・デバッグ

- **Preparation_UnityWiring.md**:
  - 「信用」と借金（+50% の利息）の説明が残っている（21-25, 95-104）。今のコードは村資金を利息なしで持ち込む。
  - スターターレリックは普通だけ、とある（27）。コードではレアとエピックも出る。
  - スタートダッシュの値段が 10万 / 15万 / 20万 とある（30）。asset は 30万 / 50万 / 70万。
  - 持ち込みの刻みが 1,000G とある（49, 67）。コードは 10,000G。
  - 信用を上げるボタン（50）は隠されている。
  - 戻るとタイトルへ、とある（94, 108）。コードは村へ戻る。
- **SaveSlot_TitleLoad_UnityWiring.md 60-61**: 「初めから → 難易度 → 確認 → スロット」とある。
  - タイトルに難易度の手順はない。難易度は出店準備で選ぶ。
- **DebugMenu_Spec.md**:
  - タブが 6 とある（21）。実際は 12。
  - `bankedGold` とある（30）。今はない。
  - 施設が 13 で 0〜3 とある（39-40）。実際は 6 施設で 0〜5。

### 新聞

- **News_UnityWiring.md §4**:
  - 「ターン頭に自動で開く」とある。そのコードはもうない。
  - 鍛冶屋からの入口を `BlackSmithView.newspaperButton` と書いている。そのフィールドはなく、実際は `ScrapStickyNotesUI.newspaperButton`。
- **News_UnityWiring.md §8.2**:
  - 壁新聞は「全社の一面から 2 本」とある。コードはタブロイド紙の 1 本と埋め草 2 本まで。
  - 「本文はクリックで開く」とある（News_Spec §10.4 も同じ）。マスは開かず、一面に上がって本文が出る。スクラップと裏取りは一面の記事にしかない。
- **News_Spec.md §9.1**:
  - スクラップの枠が「店レベル・村で増える」とある。実際は 3 のまま。
  - 枠が一杯のときに理由を出す、とある。理由は出ない。
- **News_Spec.md §8**: 購読を変えられるのは「契約更新ターンのみ」とある。空いた枠はいつでも埋められる。
- **News_Spec.md §1 D3**: 「事前の裏取りはなし」とある。酒場 Lv5 の裏取り（§9.4）はできる。

### 鍛冶屋・勇者

- **ProcurementTradingTerminal_UnityWiring.md 99, 115 / ProcurementMock_BuildPrompt.md 33**:
  - おすすめ順の並び替えで、弱点の属性が上に来る、とある（`RecommendAttributeBonus` など）。
  - その定数はない。並び替えは期待収益だけ。弱点が効くのは「次ダンジョン特化」だけ。
- **Village_Meta_Design.md 446**: 仕入れ値は「市場価格×0.7 でクランプ（`minBuyPriceRate`）」とある。
  - コードには、最低 1G の下限しかない。

### 店・設備・レリック

- **ShopMachine_UnityWiring.md**:
  - 「同じマシンは 1 台まで」とある。いくつも置ける。
  - 自動鍛造炉は「安価な武器×2/朝」とある。実際は作る品を選べて、1 日 1,000G ぶん作る。
  - 表に大型の 3 種がない。
- **ShopLevel_UnityWiring.md**:
  - オート陳列は「5 個以下」とある。実際は在庫を全部並べる。
  - 設備の枠を「将来用」と書いている。今は使っている。
- **SellOrder_UnityWiring.md**: 陳列した品は「全量」売れる、とある。
  - 実際は、その日の客の数までしか売れない（ShopEconomy.md §10.1 はこれに合っている）。
- **Relic_UnityWiring.md**:
  - レリックはイベントと配信の勝ちでだけ来る、とある。納付のたびにも、魔王側に賭けて当たったときにも来る。
  - 「スキップ」は、お金をもらって辞退すること。
- **コードのコメント**: TurnPhaseView.cs:67-68 / TurnPhase.cs / ShopUpgradePresenter.cs:6 / ShopMachinePresenter.cs:10 は、店・広告・魔王軍支援が仕入れのときに開く、と書いている。
  - プレハブでは、陳列のときのまとまりに入っている。

### 情報屋・金融・地図・納付

- **Finance_UnityWiring.md**:
  - 債券の利率が 10% / 25% とある。asset は 6% / 18%。
  - 借金の返済が 10 日ごととある（DebtPresenter.cs:99 のコメントも同じ）。実際は 7 日ごと。
  - 投資信託の価格は解禁した品だけで計算する、とある。コードは未解禁の品も入れる。
  - 属性の投資信託は 6 本で、Lv2〜3 で解禁、とある。実際は 5 本で、どれも Lv2。
- **GAS_Balance_Spec.md 141**: 金融の項目に `bondUnitCapPerInfoLevel` がない。
  - コードにはある。asset にはないので、既定の 10 が使われる。
- **DemonKingPayout_UnityWiring.md §1**: `levelUpCost` は balance.json で変えられる、とある。
  - GAS_Balance_Spec.md 103 は `levelDataList` を配信しない、と書いている。二つの設計書が食い違っている。
- **Map_Overlay_Spec.md R1**: 買っていない Lv を「Lv.???」と書いている。
  - コードの表示は「Lv.？」、Lv0 のときは「Lv.0（未出資）」。

### 配信

- **Streaming_Redesign.md**:
  - 増援は「ウェーブを 1 つ追加」もできる、とある（§4-1, §4-4）。実際は魔物を 1 体足すだけ。
  - ダンジョン側の指示は「即時」に効く、とある（§4-4）。実際はいつも魔物の手番の頭。
  - スキル・必殺で上がるのは「装備中の武器の需要」とある（§4-1）。実際は武器すべて。
  - ダンジョン側の値段が決まった額で書いてある（§4-1）。実際は防衛報酬に比例する（§9-5 は合っている）。
  - 視聴者の「お願い」スパチャ（§5-1）は実装されていない。

### 村

- **Village_Meta_Design.md §0-2 / §0-4**: 「完走ボーナス 30 万G で Lv1 を 1 つ建てられる（最安 30 万G）」とある。
  - いちばん安い Lv1 は銀行の 40 万G。ボーナスだけでは建たない。GameConstData.cs:251 のコメントも同じ書き方。
- **Village_UnityWiring.md**: 古い内容が残っている。
  - 総合 Lv の最大が 17 とある。実際は 30。
  - 換え方が「純資産×0.5」とある。
  - 区画が 13 とある。実際は 7。
  - 祠が出てくる。今はない。
  - 「出撃準備へ」とある。実際は「出店準備へ →」。

---

## 確かめられなかったこと

- **配信で上書きされる本番の値**: 費用、介入の値段、納付の額、スタートダッシュの値段、解禁の表など。スプレッドシートと配信の JSON は見ていない。
- **シーンでのつなぎ方**（コードは null でも動く作りになっている）:
  - 特殊武器タブが隠れているか
  - 行動のヒント（D8）がつながっているか
  - 売り場のマスの数
  - 新聞の壁新聞・号の送り・予想メモ・裏取りのボタン
  - Utage の Skip / BackLog / Menu が実際に見えているか（親のオブジェクトを見ていない）
- **防具だけを装備したとき**（注7）: どこまで影響するか。
- **融資のポップアップ**: `OnConfirm` / `OnCancel` を直接呼んだとき、画面が閉じるか。

---

## 実装の予定と状況

### 洗い出しで分かった、使っていなかった理由（2026-10-28）

- **勇者の装備・作戦、店 Lv、設備、鍛冶屋 Lv、情報屋 Lv、債券・投資信託、広告**: Play の自動操作に押す手順が無く、人格の質問にも無かった（モデルに一度も聞いていない。選び方ではじいていたのではない）。
- **魔王軍支援**: 質問（support）はあり、415 回答えていた。ところが自動操作は仕入れの前に「魔王軍支援」ボタンを押そうとしていた。このボタンは陳列フェーズのまとまり（`TurnPhaseView.displayGroup`）にあり、仕入れの間は隠れている。そのため押せずに黙って戻り、**一度も払われていなかった**（Editor.log に「配信前: … の魔王軍支援へ」だけが出て、「… を N 回レベルアップ」が出ない）。学習はこの判断に、払っていない結果を付けていた。
- **店 Lv・設備**: 同じく陳列フェーズのボタン。

### rules 4.2（2026-10-28 夜に入れた）

| 操作 | 自動操作 | 人格の質問（キー） | 学習の結果 | 人格の傾向 |
|---|---|---|---|---|
| 勇者の武器・防具 | 仕入れの前に勇者の画面を開き、`weaponDropdown` / `armorDropdown` で選ぶ（`weaponIds` / `armorIds` から index を引く） | `hero_weapon` / `hero_armor`（次の行き先が配信の日だけ。候補は在庫・弱点・ランクの順に 8 つまで） | 配信の翌朝までの資産（`keep` / `change`） | ずんだもん: 弱点・高ランクを 1.3 倍。めたん: 弱点 1.6 倍・在庫あり 1.3 倍・最高ランク 1.2 倍 |
| 勇者の作戦 | `tacticsDropdown`（`tacticsValues`） | `hero_tactics` | 同上 | ずんだもん: 攻撃重視 1.5 倍。めたん: バランス・防御重視を少し上げる |
| 鍛冶屋 Lv | 鍛冶屋の画面で、お任せ購入の前に `developButton` → `levelUpButton` → `weaponButton` に戻る | `smith_upgrade`（真偽） | ランの終わりまでの資産 | めたん: 使ってよい額の 3 割以内なら 2 倍、それ以外 0.4 倍 |
| 情報屋 Lv | 情報屋の `developmentButton` → `levelUpButton` | `broker_upgrade`（真偽） | 同上 | 同上 |
| 債券・投資信託 | 情報屋の `exchangeButton` → 商品の行（`ItemShopSlot.itemId`）→ `FinanceDetailPanel.quantitySlider` → `buyButton` / `sellButton` | `finance`（なし・債券・ファンド・持っているファンドを売る）＋ `finance_share`（使ってよい額の 10 / 25 / 50 / 75%） | 同上（資産に取引所の評価額を足した） | ずんだもん: ファンド 1.5 倍・債券 0.7 倍・額は 1 段上。めたん: 債券 1.5 倍・ファンド 0.7 倍・利が乗ったら売る。手持ちの下限と 3 日以内の返済は残す |
| 店 Lv | **陳列フェーズの頭**で `ShopUpgradeButton` → `upgradeButton` | `shop_upgrade`（真偽） | 同上 | 鍛冶屋 Lv と同じ |
| 設備 | 改装の画面のタブ（`tabs.machineTab`）→ `ShopMachineSlotUI.selectButton` → `purchaseButton` | `machine`（なし・置けるもの） | 同上（お金・製造・客寄せ・鮮度の種類で学ぶ） | ずんだもん: 派手な設備（客寄せ・大道芸・黄金ゴーレム・大型魔導炉）を FlashyBoost 倍。めたん: 元が取れる日数が短いお金の設備を上げ、高すぎるものを下げる |
| 魔王軍支援（直し） | 陳列フェーズの頭で押すように直した（`DebugAutoPlayer.ExternalBeforeDisplay` → `AutoPlayController.OnBeforeDisplay`） | support（今まで通り） | 今まで通り | 今まで通り |

- 「絶対しない」を作らない: 真偽の質問は true の確率を 0.12 より下げない。選択肢の質問は一様分布を 15% 混ぜる。
- 「毎日する」も作らない: 店への投資の質問は、自由な質問の固まり対策（エントロピーの下限・安全弁）から外した。
- 相談プレイでは、2 人とも「しない」のときは黙って決める。
- 押せなかったもの（お金・解禁・ボタン）は、学習には「しなかった」として付ける。結果はログの `shop_invest_result` の行。
- 一言は `PersonaLines` の `hero_weapon` / `shop_upgrade_yes` / `machine_yes` / `finance_fund` などのキー（額は G で言う。割合は言わない）。

### 次に入れるもの（学習を止めずに書き進め、6 時間ごとの立て直しのときにまとめてコンパイル）

1. 細工（F4、職人組合 Lv5。業物 / なまくら）
2. 広告（J8・J9。陳列フェーズのボタン。2 回タップで買う）
3. 新聞の購読（I5）とスクラップ・予想（I9・I11〜I13）
4. 占い師（H6）
5. 自分から納付（K2）、勝負の融資（K4）・勝負配信の宣言（L2）を断るだけにしない
6. レリックの辞退（K7）と、レリック 3 択・スターターレリックをモデルに聞く
7. 出店準備の難易度（C3）・スタートダッシュ（C5〜C7）・持ち込み資金の額（C2）
8. 設備の撤去・生産品の切り替え（J5・J6）、陳列の手動調整
