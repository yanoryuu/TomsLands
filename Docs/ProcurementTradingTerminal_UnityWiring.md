# 仕入れ画面 トレーディング端末化 — Unity 配線手順書

コード実装（Phase 0〜5）は完了済み。本書は **Unityエディタ側の手動配線** をまとめたもの。
新規UI参照はすべて null 安全なので、配線前でもビルドは通り、配線した分だけ機能が現れる。

関連スクリプト:
- 新規: `Assets/Scripts/BlackSmith/PriceChartView.cs` / `ItemDetailPanel.cs` / `ProcurementHeaderView.cs`
- 改修: `ItemShopSlot.cs` / `BlackSmithView.cs` / `BlackSmithPresenter.cs`

作業順: **コンパイル確認 → Phase 2 → 3 → 4 → 5**

---

## 0. 事前確認
- Unityエディタで Console にコンパイルエラーが無いこと（既存セーブは後方互換済み）。

---

## Phase 2 — 銘柄一覧行（ティッカー行）

対象: `BlackSmithView` の `itemShopSlotPrefab` に割り当てた `ItemShopSlot` プレハブ。

1. **行をクリック可能に（必須）**: 行のどこかに **Raycast Target = ON の Graphic**（透明Imageでも可。`rowBackground` 流用可）。`IPointerClickHandler` 実装済みのため。
2. **新規UI要素を追加し `ItemShopSlot` の Inspector に割り当て**（すべて任意・付けた分だけ表示）:

| フィールド | 種類 | 内容 |
|-----------|------|------|
| Demand Text | TextMeshProUGUI | 需要%（例「78%」） |
| Demand Bar | Slider | 需要 0〜1 バー（interactable=OFF推奨） |
| Price Trend Text | TextMeshProUGUI | 前回比矢印（↑→↓、色自動） |
| Popular Badge | GameObject | 人気バッジ |
| Low Stock Badge | GameObject | 品薄バッジ（在庫1〜2） |
| Selected Highlight | GameObject | 選択中ハイライト |

3. **レイアウト**: `[アイコン][名前][価格＋矢印][需要%＋バー][在庫][バッジ]` を横一列に。注文UI（スライダー/＋－/購入）は Phase 3 で詳細パネルへ移すので今は残置でOK。

確認: 仕入れ画面で各行に需要%・矢印・在庫・バッジ。行クリックで選択ハイライト切替＋説明更新。

---

## Phase 3 — 一覧＋詳細パネル・チャート・注文移設・並べ替え

### A. 価格チャート `PriceChartView`
1. 詳細パネル内に空の `RectTransform` を作り `PriceChartView` を追加（Graphic派生・自前描画、Image不要）。
2. サイズをチャート領域に。Inspectorで線の太さ・価格色・需要色・`Draw Demand` 調整可。

### B. 詳細パネル `ItemDetailPanel`
右ペインに親GameObjectを作り `ItemDetailPanel` を追加、各フィールドを割り当て:

| 区分 | フィールド | 内容 |
|------|-----------|------|
| ルート | Root | 選択時だけ表示する本体（未指定なら自分） |
| 基本 | Icon / Name Text / Attribute Text | アイコン・名前・属性 |
| チャート | Price Chart | A の `PriceChartView` |
| 市場分析 | Demand / BasePrice / CurrentPrice / SalesRate / WasSold / Recommend Text | 需要%・基準価・現在価格・売率・前ターン販売・おすすめ度 |
| 注文 | Quantity Slider / Quantity Text / Plus / Minus Button / Total Cost Text / Purchase Button | 数量・合計・仕入れ |

→ **行プレハブ（ItemShopSlot）から注文UI（スライダー/＋/－/購入ボタン）を削除**し、詳細パネル側に新規配置（行はティッカー表示専用に）。

### C. `BlackSmithView` への割り当て
- Item Detail Panel ← B のパネル
- Sort Dropdown ← TMP_Dropdown（任意）。**Options順を必ず `0:収益 / 1:需要 / 2:価格`**（enum index と一致）

### D. レイアウト
- 左: 既存の銘柄一覧スクロール（scrollRect）
- 右: ItemDetailPanel（チャート＋市場分析＋注文）

確認:
1. 開くと先頭銘柄が自動選択され右に詳細・チャート・注文
2. 行クリックで右の内容が切替（選択ハイライト連動）
3. 数量指定→仕入れる で所持金・在庫が増減・永続化
4. ソートDropdownで一覧順が変わる（収益＝おすすめ順）
5. 数ターン後に再度開くとチャートが折れ線として伸びる（履歴は2点以上必要。新規セーブ直後は1点）

---

## Phase 4 — 次ダンジョン情報バナー常設

### A. バナー `ProcurementHeaderView`
仕入れ画面の上部に横長バナーGameObjectを作り `ProcurementHeaderView` を追加、割り当て:

| フィールド | 種類 | 内容 |
|-----------|------|------|
| Dungeon Icon | Image | 次ダンジョンのアイコン |
| Dungeon Name Text | TextMeshProUGUI | ダンジョン名 |
| Weakness Text | TextMeshProUGUI | 「弱点:火」など |
| Turns Until Text | TextMeshProUGUI | 「あと2ターン」 |
| Hero Equip Text | TextMeshProUGUI | 「勇者Lv.5　武器:鉄剣 / 防具:革鎧」 |

### B. `BlackSmithView` への割り当て
- Procurement Header ← A のバナー

### C. GameLifetimeScope
- 変更不要（依存は全て登録済み）。

確認:
1. 開くと上部に次ダンジョン名・弱点属性・残ターン・勇者装備
2. フロー進行で残ターンが減る／次戦闘が無い位置では「次の戦闘なし」
3. 弱点属性一致の武器がおすすめ度・収益ソート上位に来やすい（オート購入も同属性優先）

---

## Phase 5 — DemandDashboard 廃止のクリーンアップ

スクリプト（`Dashboard/` 一式）は削除済み。シーン/プレハブ側の後始末:
1. **TomsShop シーン**の需要ダッシュボードのパネルGameObject（旧 `DemandDashboardView` と配下の `DemandDashboardSlot`）を削除（Missing Script 化するため）。
2. ホーム画面の**ダッシュボードを開くボタン**を削除（`TomsShopView.DashboardButton` 参照は除去済み）。
3. `GameLifetimeScope` Inspector に `demandDashboardView` 欄が無いことを確認（フィールド削除済み）。

確認: ダッシュボード画面が消え、需要確認は仕入れ画面の一覧/詳細で代替できている。コンパイルエラーなし。

---

## 調整できるパラメータ（コード定数）
- おすすめスコア重み: `ItemModel.RecommendTrendWeight = 0.5f` / `RecommendAttributeBonus = 1.5f`
- 履歴保持ターン数: `RuntimeItemData.ShopHistoryCapacity = 12`
- チャートの線の太さ・色: `PriceChartView` の Inspector
