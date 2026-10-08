# 朝刊（ニュース）Unity 手動配線手順

最終更新: 2026-09-21
対象: フェーズ1（最小版）。仕様は `Docs/News_Spec.md`、パーツは `Docs/News_UIParts.md`。

**フェーズ1は完了**。プレハブ作成・シーン配置・Inspector 配線・開く導線まで実装し、実機描画を確認済み。

実機の描画結果:
- 朝刊画面 `Docs/mockups/news_impl_phase1.png`
- 仕入れ画面の朝刊ボタン `Docs/mockups/news_blacksmith_button.png`

---

## 0. 実装済みのもの（配線不要）

| 分類 | 内容 |
|---|---|
| 未来情報の遮断 | `ItemModel.GetRecommendScore` を削除し `ExpectedRevenueOf`（現在値のみ）へ統一。弱点属性は `DungeonIntelModel` で開示を制御。預言者の Trend ランキングは `MarketHeat` へ差し替え |
| マスター | `Assets/Resources_moved/Newspapers.csv`（5社）/ `NewsArticles.csv`（10本）。**タブ区切り・拡張子は .csv**。Addressables 登録済み |
| ロジック | `NewsMasterLoader` / `NewsScheduler` / `NewsModel` / `NewsEffectResolver` / `DungeonIntelModel` |
| 経済への接続 | `ItemModel.ApplyShopTurnEconomy(..., NewsEffectResolver news = null)`。`GameFlowManager` から渡している |
| DI | `NewsModel` / `NewsEffectResolver` / `DungeonIntelModel` を `GameLifetimeScope` に登録済み |
| フェーズ | `TomsShopGamePhase.Newspaper` を追加。`GamePanelManager` に `newspaperPanel` の分岐を追加 |
| UI コード | `NewspaperView` / `NewsArticleCellUI` / `NewspaperPresenter` |

**`newspaperView` が未設定なら `NewspaperPresenter` は登録されない**ので、配線前でも既存の動作は壊れない。

---

## 1. 記事セルのプレハブ（完了）

`Assets/Prefabs/NewsArticleCell.prefab` を作成済み。構成は下記。

```
NewsArticleCell            … Image(13記事ボックス小) + Button + VerticalLayoutGroup + ContentSizeFitter(縦Preferred)
├ Headline                 … TMP 28pt 濃茶
├ Lead                     … TMP 20pt 濃茶
├ Byline                   … TMP 16pt 中茶
├ NewBadge                 … Image(16NEWバッジ)
└ BodyRoot                 … 既定で非アクティブ
   └ Body                  … TMP 20pt 濃茶
```

`NewsArticleCellUI` の参照:

| フィールド | 割り当て |
|---|---|
| headlineText | Headline |
| leadText | Lead |
| bylineText | Byline |
| bodyText | Body |
| bodyRoot | BodyRoot |
| newBadge | NewBadge |
| rootButton | ルートの Button |

---

## 2. 朝刊パネル（完了）

`Assets/Prefabs/Screens/Newspaper.prefab` を作成済み。既存パネルに合わせ **ルート(Transform) → Canvas → NewspaperPanel** の3層構造。**CommonView は置かない**（鍛冶屋・情報屋と同じ扱い。所持金とターンは題字バーが持つ）。

```
Newspaper                  … 全画面
├ Backdrop                 … Image(1枠)
├ Masthead                 … Image(9題字枠)
│  ├ MastheadText          … TMP 56pt 濃茶
│  ├ IssueText             … TMP 24pt
│  └ MoneyText             … TMP 24pt 金
├ PaperBase                … Image(8紙面ベース)
│  └ Scroll                … ScrollRect
│     └ Viewport/Content   … VerticalLayoutGroup + ContentSizeFitter ← articleParent
└ CloseButton              … Image(9−0閉じる/鍛冶屋の流用)
```

`NewspaperView` の参照:

| フィールド | 割り当て |
|---|---|
| mastheadText | MastheadText |
| issueText | IssueText |
| moneyText | MoneyText |
| articleParent | Scroll/Viewport/Content |
| articleCellPrefab | `NewsArticleCell.prefab` |
| closeButton | CloseButton |

---

## 3. シーンへの組み込み（完了・TomsShop.unity）

1. 他の画面プレハブと同じ階層に `Newspaper.prefab` を配置し、**非アクティブ**にしておく
2. `GamePanelManager` の `newspaperPanel` に配置したオブジェクトを割り当てる
3. `GameLifetimeScope` の `newspaperView` に `NewspaperView` を割り当てる
4. シーンを保存

---

## 4. 朝刊を開く導線（完了）

**ターン頭に自動で開く**
`TomsShopPresenter.Entry` の冒頭、ターン番号を捕捉した直後に `Newspaper` へ遷移する。
1ターンに一度だけ（`_lastNewspaperTurn`）。

挿入位置が重要で、**「保留イベント」「借金パネル」「朝レポート」より前**でなければならない。
朝レポートは消費型（`Consume()` で消える）なので、表示してから画面を切り替えると内容が失われる。
また抜ける時点では `_lastKnownTurn` をまだ更新していないため、朝刊を閉じて Shop に戻ったとき
`turnChanged` が再び true になり、ターン頭の処理が正しく走る。

**仕入れ画面から読み返す**
`BlackSmith.prefab` の「もどる」の右隣に `NewspaperButton`（朝刊）を追加。
`BlackSmithView.newspaperButton` / `OnNewspaperRequested` 経由で `BlackSmithPresenter` が遷移させる。

**戻り先**
`NewspaperPresenter` は `StateManager.CurrentTomsShopPhase` を購読し、朝刊以外のフェーズを
戻り先として記憶する。ターン頭から開けば Shop、仕入れ画面から開けば BlackSmith に戻る。

**未配線でも壊れない**
`StateManager.HasHandler(TomsShopGamePhase)` を追加し、受け手がいるフェーズにしか遷移しない。
`newspaperView` が未設定なら `NewspaperPresenter` は登録されないので、遷移そのものが起きない。

---

### 9スライスの注意（実作業で踏んだ）

**境界は「絵に描かれている角丸の半径以上」にすること。** 小さいと角の円弧が中央へ引き伸ばされ、
枠の内側に二重の輪郭が出て文字に重なる。逆に**要素側の余白は境界より大きく**取らないと、
文字が枠の下に潜る。確定値:

| スプライト | 境界 | 使う側の余白 |
|---|---|---|
| 1枠 | 95 | — |
| 8紙面ベース | 85 | Scroll を 84/86 内側へ |
| 9題字枠 | 70 | 号数・所持金を 112 内側へ |
| 13記事ボックス小 | 78 | セルの padding 96/96/56/48 |

文字は **MPLUSRounded1c-Bold SDF**`NoOutLine` を使う。縁あり版は小さい文字が潰れる。

## 5. 動作確認

1. 新規に周を始める（シードが決まるとカレンダーが組まれる）
2. 朝刊を開き、記事が2〜3本並ぶこと。クリックで本文が開き、NEWバッジが消えること
3. 数ターン進め、記事に書かれた「原因」に対応する銘柄の需要が**数ターン遅れて**上がること
   - 確認しやすいのは N001（氷結種→火の武器）と N002（火山→水の鎧）
   - Debug メニュー（F12）の「相場」タブで全銘柄の需要・価格を一覧できる
4. 仕入れ画面のバナーが `弱点:?` になっていること。情報屋でそのダンジョンの情報を買うと `弱点:火` などに変わること

---

## 6. フェーズ1時点の既知の割り切り

| 項目 | 現状 |
|---|---|
| 記事が10本しかない | 3ターンで一巡するので**同じ記事がすぐ再登場する**。100本入れれば解消する |
| 効果が飽和する | 記事が重なると `TrendBias` が上限 1.0 に張り付く。本数が増えれば起きにくいが、上限の扱いは要調整 |
| 購読 | 未実装。全社の記事が無条件で並ぶ（フェーズ4） |
| クロスリファレンス | `crossRefGroup` 列は持っているが、スケジューラはまだ使っていない（フェーズ3） |
| 社ごとの誤報率 | `falseRate` 列は持っているが未使用。誤報は記事の `truth` 固定（フェーズ3） |
| 訂正・結果記事 | フェーズ2で実装済み（下記 §7） |
| スクラップ・確認 | 未実装（フェーズ5） |

---

## 7. フェーズ2（2段効果・結果記事・訂正記事）— 配線不要

**コードとマスターだけで完結**。シーン・プレハブ・`GameLifetimeScope` の変更はない。

| 分類 | 内容 |
|---|---|
| 目盛り | `Assets/Scripts/Newspaper/NewsTuning.cs` を新設。面ごとの hypeRate、小/中/大の kick・duration、誇張倍率、跳ねの剥がれターン数、続報の遅れ |
| マスター | `demandKick` / `hypeRate` / `durationTurns` を空欄にすると目盛りの既定値が入る。既存10本は trendDelta を目盛り（0.3/0.6/1.0）に揃え、残り3列を空欄化 |
| 記事 | 通常 N011〜N013（誤報2・誇張1）、結果記事 R001/R002/R005/R009/R013、訂正記事 C007/C011/C012 を追加（計21本） |
| 効果 | 誇張は2段目 ×0.5。跳ねは発効ターンから3ターンで剥がれる（誤報はここで値だけ落ちる） |
| 紙面 | `NewspaperPresenter` が 一面→二面→市況→うわさ→結果→訂正 の順に並べる。訂正は最後（隅） |

### 動作確認

1. 新規に周を始め、数ターン進める
2. N007（墓地の人影）/ N011（精霊祭）/ N012（西方傭兵団）が載ったら、掲載から lead + duration + 1 ターン後に
   「お詫びと訂正」記事が紙面の最後に出ること
3. N001 / N002 / N005 / N009 / N013 が載ったら、lead + duration ターン後に結果記事が出ること
4. Debug メニュー（F12）の「相場」タブで、誤報の対象銘柄が**掲載日に跳ね、発効予定日から下がる**こと
   （N011 は風の防具、N012 は闇の武器、N007 は闇の武器）
5. ログ `[ShopEconomy]` の price が、記事のない銘柄では従来と同じ動きであること

---

## 8. 3ペイン化（購読・スクラップ・相場）— 2026-10-08 実装・プレハブ組み立て・Play 確認済み

フェーズ4（購読と枠）・フェーズ5（スクラップと確認）のロジックと、C案の左右ペインの View を実装した。
プレハブは §8.5 のとおり組み立て済み（`Newspaper.prefab` を3ペインに組み直し、部品は `Assets/Prefabs/News/`）。

### 8.1 追加・変更したコード

| ファイル | 内容 |
|---|---|
| `Newspaper/NewspaperSubscriptionModel.cs` | 契約（社・開始・終了ターン）。枠・二重契約・所持金の判定。**契約中は解約・乗り換え不可、自動更新なし**（切れたターンに枠が空き、そこで選び直す＝更新ターンのみ変更可） |
| `Newspaper/ScrapbookModel.cs` | ピン留め（枠3）・状態（保留/未確認/○/×）・確認で名鑑へ移動・社の実測的中率 |
| `Newspaper/NewspaperSaveStore.cs` | `newspaperData.json`（`SaveSlotManager.GetPath`）。契約・スクラップ・名鑑・実測・**既読**。`runSeed` が違えば読み捨て |
| `Newspaper/NewsMarketSummary.cs` | 相場欄の集計。種別ごとに ShopPriceHistory を等ウェイト指数化、5ターン変動率、MarketHeat 平均と5段階 |
| `Newspaper/UI/*.cs` | `NewspaperCompanyCardUI` / `NewsRatingBarUI` / `NewsLeadArticleUI` / `NewsScrapCardUI` / `NewsMarketRowUI` / `NewsPageTabUI` |
| `NewspaperView.cs` / `NewspaperPresenter.cs` | 3ペイン化。旧フィールド名（mastheadText 等）は維持 |
| `NewsTuning.cs` | 購読枠（確定 Lv1〜2:1 / Lv3〜4:2 / Lv5:3）、壁新聞2本、確認 1,500G / 3件 4,000G、枠3 |
| `NewsScheduler.cs` / `NewsModel.cs` | 掲載キー `NewsIssueEntry.Key`（ターン:社:記事）と `FindEntry`。**カレンダー生成は無変更**（経済は従来と同一） |
| `GameLifetimeScope.cs` | `NewspaperSubscriptionModel` / `ScrapbookModel` を Singleton 登録（2行） |
| `RunSaveCleaner.cs` | 新規ランで `newspaperData.json` を削除（1行） |
| `MorningReportModel.cs` | `Peek()`（自店面が消費せずに読む。1メソッド） |

### 8.2 画面の振る舞い

- 開いたとき: 前回読んでいた社がまだ購読中ならその社、無ければ最初の購読社、契約0件なら**壁新聞**（全社の一面記事から2本）
- 左: 未購読カードの「購読する」で即契約（一括払い・所持金保存）。購読中カードのクリックで読む社を切替
- 中央: 面タブは社の `pages` ＋「自店」。社が持たない面の記事（結果記事など）は一面へ寄せる。
  訂正記事は面に関係なく**一面の「お詫びと訂正」枠**へ。小記事クリックで一面トップへ差し替わり本文が開く
- 右上: 一面トップの「スクラップする」で貼る（通常記事のみ。満杯・貼り済みは押せない）。
  決着後のカードにだけ「確認 1,500G」。未確認が3件そろうと「まとめて確認 4,000G」。確認した日はカードに答えを出したまま枠を空ける
- 右下: 行ごとに `NewsMarketRowUI.itemType` で集計対象を決める

### 8.3 プレハブ組み立て設計（1920×1080）

```
y  20 ┌ 上部バー h100 ───────────────────────────────────────────────┐
      │[閉じる] 情報ターミナル   [2日付: 第12号]  次の戦闘まで 3日   [3ゴールド: 48,600G] │
 140  ├ 左 x30–430 ─┐ ┌ 中央 x450–1470 ─────────────┐ ┌ 右 x1490–1890 ┐
      │ 1枠         │ │ 8紙面ベース y140–960           │ │ 19台紙 y140–640 │
      │ 社カード×5   │ │  9題字枠 h96（紋章80×2）        │ │  29見出し「スクラップ3/3」│
      │ 384×140     │ │  10−1罫線太 h8                │ │  カード 360×116 ×3 │
      │ 間隔8       │ │  一面トップ 12記事ボックス大 h270 │ │  24まとめて確認 300×56 │
      │             │ │  小記事 2×2 Grid 460×140 間隔10 │ ├ 1枠 y656–1040 ─┤
 908  │ 7プレート h120│ │  17お詫びと訂正 h64            │ │  29見出し「相場」    │
1040  └─────────────┘ └ 面タブ行 y966–1040 ─────────────┘ │  25相場行 360×72 ×3〜4│
                       [28左] 11面タブ 160×66 ×5 [28右]       └─────────────────┘
```

| ノード | 部品 | 寸法・設定 | 参照先（View フィールド） |
|---|---|---|---|
| TopBar/Close | 鍛冶屋 `9−0閉じる` | 80×80 | closeButton |
| TopBar/Title | TMP 32pt 濃茶「情報ターミナル」 | 固定文字 | — |
| TopBar/Issue | `基本/2日付` ＋ TMP 28pt | 260×80 | issueText |
| TopBar/NextBattle | TMP 28pt | 中央 | nextBattleRoot / nextBattleText |
| TopBar/Money | `基本/3ゴールド` ＋ TMP 28pt 金 | 300×80 右寄せ | moneyText |
| Left/Cards | VerticalLayoutGroup（spacing 8, padding 8/8/16/0） | 400×744 | companyCardParent |
| Left/SlotPlate | `7購読枠プレート` ＋ TMP 30pt「購読枠 2/3」＋ TMP 20pt | 384×120 | slotText / renewalText |
| Center/Paper/Masthead | `9題字枠`（境界70）＋左右に紋章 Image 80×80 ＋ TMP 56pt | 956×96 | mastheadText / mastheadEmblems |
| Center/Paper/Articles | 記事群のまとめ（自店面で隠す） | — | paperArticlesRoot |
| …/Lead | `12記事ボックス大`（**境界未設定→要設定 約60**）＋ NewsLeadArticleUI | 956×270 | leadArticle |
| …/Lead 内 | 見出し TMP 40 / リード 20（2行）/ Body(TMP 20, 既定非表示) / Photo(`14写真枠`, 任意) / 署名 `15署名ボックス` 240×40 / `18スクラップする` 200×52（SpriteSwap 18−1） | | |
| …/Grid | ScrollRect＞Content: GridLayoutGroup cell 470×140 spacing 10 | 956×290 | articleParent |
| …/Grid セル | `NewsArticleCell` の小型版（13記事ボックス小、**pixelsPerUnitMultiplier 2**で境界を39相当に、padding 28/28/18/14、ContentSizeFitter 外す） | 470×140 | articleCellPrefab |
| …/Correction | `17お詫びと訂正枠` ＋ TMP 18「お詫びと訂正」（固定）＋ TMP 15 | 956×64 | correctionRoot / correctionText |
| Center/Paper/ShopPage | TMP 22 左上揃え | 紙面内いっぱい | shopPageRoot / shopReportText |
| Center/Tabs | HorizontalLayoutGroup: `28ページ送り左` 64×64 ＋ NewsPageTabUI×5（front/second/market/rumor/shop、`11−0/11−1`、TMP 26） ＋ `28右` | 1020×74 | pageTabs / prevPageButton / nextPageButton |
| Right/Scrap | `19スクラップ台紙` ＋ `29見出しラベル` 220×52（上辺に半分かぶせる）＋ TMP 24 | 400×500 | scrapHeaderText |
| …/Cards | VerticalLayoutGroup spacing 10, top 60 | 360×368 | scrapCardParent |
| …/Batch | `24−0/24−1まとめて確認` ＋ TMP 20 | 300×56 | batchConfirmButton / batchConfirmLabel |
| Right/Market | `1枠` ＋ `29見出しラベル`「相場」 | 400×384 | — |
| …/Row×3（武器/防具/道具） | NewsMarketRowUI（itemType を行ごとに設定） | 360×72 | marketRows |

**部品プレハブ**

- `NewspaperCompanyCard.prefab`（384×140）: 背景 `2−0/2−1社カード` / 紋章 88×88（左, x34） / 社名 TMP 26 / 料金 TMP 20 金「20,000G / 5ターン」/
  信頼・速報・網羅: ラベル TMP 16 ＋ NewsRatingBarUI（`3−0/3−1星` 18×16 ×5, spacing 3）/ 実測バー（Image Filled 横, 高さ4, 信頼の真下, 任意）/
  `5購読中リボン` 72×58 右上＋TMP 16「購読中」/ `4−0購読する`（SpriteSwap 4−1）120×50 右下＋TMP 20「購読する」/ ルート Button
  - 社カードの PNG は**上 約90px が透明**（リボンの逃げ）。9スライス（例 L40 R40 T130 B40）にするかスプライト矩形で切ってから使う
  - `emblems` に5社の紋章を登録（royal=王立官報 / guild=冒険者ギルド報 / commerce=週刊商業新聞 / tabloid=街角かわら版 / craft=職人季報）
- `NewsScrapCard.prefab`（360×116）: `20スクラップカード` / `21留め具` 22×36 上中央 / バッジ `22−2/22−1/22−0` 64×64 左 /
  見出し TMP 20 / リード TMP 15（2行で切る）/ `23−0確認`（SpriteSwap 23−1）96×56 右＋TMP 15「確認\n1,500G」
- 相場行: `25相場行` / 種別アイコン 48×48 / TMP 20 種別名 / `27スパークライン枠` 120×44 の内側に PriceChartView（drawDemand=false, lineThickness 2, padding 4）/
  TMP 20 変動率 / `26Heat` ドット 24×24（heatSprites に 凪→荒れ の順）/ TMP 18 Heat 語

### 8.4 組み立て結果（2026-10-08・完了）

- 部品プレハブ: `Assets/Prefabs/News/NewspaperCompanyCard.prefab`（392×152）/ `NewsScrapCard.prefab`（372×118）/ `NewsArticleCellSmall.prefab`（458×150）。`Newspaper.prefab` は3ペインに組み直し、View 全参照・紋章5社を配線済み
- 9スライス境界を設定: 記事ボックス大・台紙・スクラップカード・訂正枠・相場行・プレート・見出し・面タブ・確認系ボタン・署名・スパークライン枠
- 書体は **`MPLUSRounded1c-Bold SDFPlusPaddingNoOutLine`**（他画面の本文と同じ）。`SDFNoOutLine` のマテリアルは UNDERLAY_ON で、小さい文字に箱状の影が出る
- 素材の手直し: `2−0/2−1社カード` は透明帯の位置が状態で上下逆だったので本体だけに切り詰め（Sprite Mode を Single へ）。`4−0/4−1購読する` も各自の外接矩形で切り詰め
- 追加素材: `22−3状態バッジ誤報.png`（×。確定バッジと同じ円形、配色規定の RED）
- 一面トップは挿絵なし・本文を常時表示。一面に記事が無い日は、記事のある最初の面を開く
- 相場は 武器 / 防具 / 全体 の3行（道具の銘柄がマスターに無いため）
- 通し確認のキャプチャ: `Assets/Screenshots/news_3pane_01_wall` 〜 `08_confirmed.png`

**Play 確認のハマりどころ**: エディタが非フォーカスだと Play のフレームが進まない（`Time.frameCount` が止まる）。
`EditorApplication.isPaused = true` → `EditorApplication.Step()` を必要回数 → `isPaused = false` で同期的に進められる。
`playModeStartScene` はドメインリロードで BootScene に戻る。

