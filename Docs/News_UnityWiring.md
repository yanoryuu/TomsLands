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
| 訂正・結果記事 | `followUpId` 列は持っているが未使用（フェーズ2） |
| スクラップ・確認 | 未実装（フェーズ5） |
