# Unity Editor セットアップガイド

このチャットで実装した全機能のUnity Editor内での設定手順をまとめます。

---

## 目次

1. [バグ修正：広告ステータス保存](#バグ修正広告ステータス保存)
2. [機能①：オート購入（鍛冶屋）](#機能オート購入鍛冶屋)
3. [機能③：ワンタップおすすめ陳列](#機能ワンタップおすすめ陳列)
4. [機能④：前回と同じボタン（配信設定）](#機能前回と同じボタン配信設定)
5. [機能⑤：需要ダッシュボード](#機能需要ダッシュボード)
6. [機能A：因果の見える化（ターン終了サマリー）](#機能a因果の見える化ターン終了サマリー)
7. [機能B：ターン行動チェックリスト](#機能bターン行動チェックリスト)

---

## バグ修正：広告ステータス保存

**Editor変更なし。** コードのみの修正。

---

## 機能①：オート購入（鍛冶屋）

### 対象：鍛冶屋パネル（BlackSmith パネル）

`BlackSmithView` コンポーネントが付いた GameObject に以下を追加します。

#### 1. ボタンを追加

鍛冶屋UI上の任意の場所に **Button** を新規作成します。

```
[推奨配置] 鍛冶屋パネル内の上部または下部に「オート購入」ボタン
```

| プロパティ | 設定値 |
|---|---|
| Button テキスト | `オート購入` |

#### 2. 結果表示テキストを追加

ボタンの近くに **TextMeshPro - Text (UI)** を新規作成します。

| プロパティ | 設定値 |
|---|---|
| Text の初期値 | （空欄でOK） |
| Font Style | Normal |
| Font Size | 14〜16 程度 |

#### 3. BlackSmithView の Inspector に割り当て

`BlackSmithView` コンポーネントの Inspector を開き：

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Auto Buy Button` | 手順1で作成したButton |
| `Auto Buy Result Text` | 手順2で作成したTextMeshProUGUI |

---

## 機能③：ワンタップおすすめ陳列

### 対象：陳列設定パネル（ItemSelectionView）

#### 1. ボタンを追加

陳列設定UI上の任意の場所に **Button** を新規作成します。

```
[推奨配置] 陳列リスト上部の「おすすめ陳列」ボタン
```

| プロパティ | 設定値 |
|---|---|
| Button テキスト | `おすすめ陳列` |

#### 2. ItemSelectionView の Inspector に割り当て

`ItemSelectionView` コンポーネントの Inspector を開き：

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Auto Display Button` | 手順1で作成したButton |

---

## 機能④：前回と同じボタン（配信設定）

### 対象：配信設定パネル（StreamingSettingView）

#### 1. ボタンを追加

配信設定UI上の「確定」ボタンの近くに **Button** を新規作成します。

```
[推奨配置] 確定ボタンの左隣または上
```

| プロパティ | 設定値 |
|---|---|
| Button テキスト | `前回と同じ` |
| 初期表示 | **非表示（SetActive: false）** ← コードで制御するため |

#### 2. ラベルテキストを追加（ボタンの子オブジェクト or 別TMP）

ボタン内のテキストとして **TextMeshPro - Text (UI)** を使います。
Buttonの子の既存テキストを利用してもよいですし、別途作成しても構いません。

| プロパティ | 設定値 |
|---|---|
| 初期テキスト | `前回と同じ` |

#### 3. StreamingSettingView の Inspector に割り当て

`StreamingSettingView` コンポーネントの Inspector を開き：

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Quick Confirm Button` | 手順1で作成したButton |
| `Quick Confirm Label` | 手順2のTextMeshProUGUI |

---

## 機能⑤：需要ダッシュボード

この機能は **新規Prefab2つ** と **シーン設定** が必要です。

### A. DashboardButton を TomsShopView に追加

#### 1. ボタンを追加

TomsShopメイン画面に **Button** を新規作成します。

```
[推奨配置] 右上や下部ナビゲーション部分に「需要」ボタン
```

| プロパティ | 設定値 |
|---|---|
| Button テキスト | `需要` または `📊` |

#### 2. TomsShopView の Inspector に割り当て

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Dashboard Button` | 手順1で作成したButton |

---

### B. DemandDashboardSlot プレハブを作成

`Assets/Prefabs/` などに `DemandDashboardSlot.prefab` を作成します。

#### GameObject 構成

```
DemandDashboardSlot (DemandDashboardSlot.cs)
├── Icon (Image)
├── ItemNameText (TextMeshProUGUI)
├── AttributeText (TextMeshProUGUI)
├── DemandSlider (Slider)
├── DemandText (TextMeshProUGUI)
├── PriceText (TextMeshProUGUI)
├── PriceTrendText (TextMeshProUGUI)
├── StockText (TextMeshProUGUI)
├── DisplayToggleButton (Button)
│   └── DisplayToggleBg (Image) ← Buttonと同GameObjectのImageでも可
└── Badges
    ├── BadgeHighDemand (GameObject) ← "高需要"ラベルなど
    ├── BadgeTrendUp (GameObject)    ← "上昇中"ラベルなど
    ├── BadgePriceUp (GameObject)    ← "価格↑"ラベルなど
    └── BadgeLowStock (GameObject)   ← "在庫少"ラベルなど
```

#### DemandDashboardSlot.cs の Inspector フィールドに割り当て

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Item Icon` | Icon の Image |
| `Item Name Text` | ItemNameText の TextMeshProUGUI |
| `Attribute Text` | AttributeText の TextMeshProUGUI |
| `Demand Slider` | DemandSlider の Slider |
| `Demand Text` | DemandText の TextMeshProUGUI |
| `Price Text` | PriceText の TextMeshProUGUI |
| `Price Trend Text` | PriceTrendText の TextMeshProUGUI |
| `Stock Text` | StockText の TextMeshProUGUI |
| `Display Toggle Button` | DisplayToggleButton の Button |
| `Display Toggle Bg` | DisplayToggleBg の Image |
| `Badge High Demand` | BadgeHighDemand の GameObject |
| `Badge Trend Up` | BadgeTrendUp の GameObject |
| `Badge Price Up` | BadgePriceUp の GameObject |
| `Badge Low Stock` | BadgeLowStock の GameObject |

> **Badge の各GameObjectには "高需要" などのテキストラベルをつけておくと分かりやすいです。**

---

### C. DemandDashboardView パネルをシーンに追加

Canvasの子にダッシュボード全体のパネルを作成し、`DemandDashboardView.cs` をアタッチします。

```
DemandDashboard (DemandDashboardView.cs)
└── DashboardPanel (GameObject) ← dashboardPanelに割り当て
    ├── CloseButton (Button)
    ├── SortButtons
    │   ├── SortByRevenueButton (Button)  ← "売上順"
    │   ├── SortByDemandButton (Button)   ← "需要順"
    │   └── SortByPriceButton (Button)    ← "価格順"
    ├── FilterButtons
    │   ├── FilterAllButton (Button)      ← "全て"
    │   ├── FilterWeaponButton (Button)   ← "武器"
    │   └── FilterArmorButton (Button)    ← "防具"
    └── ScrollView
        └── Viewport
            └── Content (Transform) ← slotParentに割り当て
```

#### DemandDashboardView.cs の Inspector フィールドに割り当て

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Dashboard Panel` | DashboardPanel の GameObject |
| `Close Button` | CloseButton の Button |
| `Slot Parent` | ScrollView/Viewport/Content の Transform |
| `Slot Prefab` | 手順Bで作成した DemandDashboardSlot.prefab |
| `Sort By Revenue Button` | SortByRevenueButton の Button |
| `Sort By Demand Button` | SortByDemandButton の Button |
| `Sort By Price Button` | SortByPriceButton の Button |
| `Filter All Button` | FilterAllButton の Button |
| `Filter Weapon Button` | FilterWeaponButton の Button |
| `Filter Armor Button` | FilterArmorButton の Button |

> **DashboardPanel はデフォルトで非表示（SetActive: false）にしておきます。**

---

### D. GameLifetimeScope に割り当て

`GameLifetimeScope` コンポーネントの Inspector を開き：

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Demand Dashboard View` | 手順Cで作成した DemandDashboard の DemandDashboardView |

> **この欄が空欄の場合、ダッシュボード機能は無効化されます（エラーは出ません）。**

---

## 機能A：因果の見える化（ターン終了サマリー）

### 対象：TurnEndSummaryRowUI プレハブ

既存の `TurnEndSummaryRow.prefab`（またはrowPrefabとして設定されているプレハブ）を編集します。

#### 1. causeText 用テキストを追加

行UIに **TextMeshPro - Text (UI)** を新規追加します。

```
[推奨配置] trendText（↑↓→）の右隣または下
```

| プロパティ | 設定値 |
|---|---|
| Font Size | 12〜14 程度 |
| 初期テキスト | （空欄でOK） |

表示される文字と色：

| 状態 | テキスト | 色 |
|---|---|---|
| 流行が後押し | `流行中！` | オレンジ |
| 陳列で上昇 | `陳列効果` | 緑 |
| 流行逆風で下落 | `流行下落` | 青 |
| 変動なし | `安定` | グレー |

#### 2. TurnEndSummaryRowUI の Inspector に割り当て

`TurnEndSummaryRowUI` コンポーネントの Inspector を開き：

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Cause Text` | 手順1で追加した TextMeshProUGUI |

---

## 機能B：ターン行動チェックリスト

この機能は **新規Prefab1つ** と **シーン上のViewオブジェクト** が必要です。

### A. TurnActionHintItemUI プレハブを作成

`Assets/Prefabs/` などに `TurnActionHintItem.prefab` を作成します。

#### GameObject 構成

```
TurnActionHintItem (TurnActionHintItemUI.cs)
├── Background (Image) ← backgroundImageに割り当て（色でプライオリティを表現）
├── MessageText (TextMeshProUGUI)
└── ActionButton (Button) ← タップで対象画面へ遷移
```

背景色はコードで自動設定されますが、初期色は任意でOKです：

| プライオリティ | 色（参考） |
|---|---|
| Critical（赤） | `(0.85, 0.15, 0.15, 0.90)` |
| Warning（黄） | `(0.90, 0.65, 0.05, 0.90)` |
| Info（青） | `(0.15, 0.50, 0.85, 0.90)` |

#### TurnActionHintItemUI.cs の Inspector フィールドに割り当て

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Message Text` | MessageText の TextMeshProUGUI |
| `Background Image` | Background の Image |
| `Action Button` | ActionButton の Button |

> **ActionButton の onClick は Inspector では設定不要。コードで自動登録されます。**

---

### B. TurnActionHintView をシーンに配置

TomsShopメイン画面（Shopフェーズで表示されるCanvas）の子に **TurnActionHintView** オブジェクトを作成し `TurnActionHintView.cs` をアタッチします。

```
TurnActionHintView (TurnActionHintView.cs)
└── HintContainer (Transform) ← VerticalLayoutGroup 推奨
```

#### 推奨 HintContainer 設定

| コンポーネント | 設定値 |
|---|---|
| `VerticalLayoutGroup` をアタッチ | |
| Spacing | 8 |
| Child Alignment | Upper Left |
| Control Child Size (Height) | チェックあり |
| Use Child Scale | チェックなし |
| Child Force Expand | チェックなし |

#### TurnActionHintView.cs の Inspector フィールドに割り当て

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Hint Container` | HintContainer の Transform |
| `Hint Item Prefab` | 手順Aで作成した TurnActionHintItem.prefab |

---

### C. GameLifetimeScope に割り当て

`GameLifetimeScope` コンポーネントの Inspector を開き：

| フィールド名 | 割り当てるオブジェクト |
|---|---|
| `Turn Action Hint View` | 手順Bで作成した TurnActionHintView |

> **この欄が空欄の場合、ヒント機能は無効化されます（エラーは出ません）。**

---

## 設定チェックリスト

全機能の設定完了を確認します。

### GameLifetimeScope の Inspector

| フィールド | 割り当て済み？ |
|---|---|
| `Demand Dashboard View` | ☐ |
| `Turn Action Hint View` | ☐ |

### BlackSmithView の Inspector

| フィールド | 割り当て済み？ |
|---|---|
| `Auto Buy Button` | ☐ |
| `Auto Buy Result Text` | ☐ |

### ItemSelectionView の Inspector

| フィールド | 割り当て済み？ |
|---|---|
| `Auto Display Button` | ☐ |

### StreamingSettingView の Inspector

| フィールド | 割り当て済み？ |
|---|---|
| `Quick Confirm Button` | ☐ |
| `Quick Confirm Label` | ☐ |

### TomsShopView の Inspector

| フィールド | 割り当て済み？ |
|---|---|
| `Dashboard Button` | ☐ |

### DemandDashboardView の Inspector

| フィールド | 割り当て済み？ |
|---|---|
| `Dashboard Panel` | ☐ |
| `Close Button` | ☐ |
| `Slot Parent` | ☐ |
| `Slot Prefab` | ☐ |
| `Sort By Revenue Button` | ☐ |
| `Sort By Demand Button` | ☐ |
| `Sort By Price Button` | ☐ |
| `Filter All Button` | ☐ |
| `Filter Weapon Button` | ☐ |
| `Filter Armor Button` | ☐ |

### DemandDashboardSlot の Inspector（プレハブ）

| フィールド | 割り当て済み？ |
|---|---|
| `Item Icon` | ☐ |
| `Item Name Text` | ☐ |
| `Attribute Text` | ☐ |
| `Demand Slider` | ☐ |
| `Demand Text` | ☐ |
| `Price Text` | ☐ |
| `Price Trend Text` | ☐ |
| `Stock Text` | ☐ |
| `Display Toggle Button` | ☐ |
| `Display Toggle Bg` | ☐ |
| `Badge High Demand` | ☐ |
| `Badge Trend Up` | ☐ |
| `Badge Price Up` | ☐ |
| `Badge Low Stock` | ☐ |

### TurnEndSummaryRowUI の Inspector（プレハブ）

| フィールド | 割り当て済み？ |
|---|---|
| `Cause Text` | ☐ |

### TurnActionHintItemUI の Inspector（プレハブ）

| フィールド | 割り当て済み？ |
|---|---|
| `Message Text` | ☐ |
| `Background Image` | ☐ |
| `Action Button` | ☐ |

### TurnActionHintView の Inspector

| フィールド | 割り当て済み？ |
|---|---|
| `Hint Container` | ☐ |
| `Hint Item Prefab` | ☐ |
