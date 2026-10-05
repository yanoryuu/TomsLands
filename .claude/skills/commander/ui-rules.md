# UIルール（UIタスク委譲時に「必須ルール」を貼る）

## 必須ルール（そのままプロンプトに貼る）
```
【UI必須ルール】
- 見た目にこだわる。完成後キャプチャで確認し、改善点を報告する
- 不要な文字（説明文・装飾ラベル・見出し・注釈・プレースホルダ文）を追加しない。表示するテキストは仕様で必要なものだけ
- 既存UIのトーンに合わせる: 木と金縁のフラットなカートゥーン調。新規素材より既存スプライト・プレハブの再利用を優先
- フォントは Assets/Font/MPLUSRounded1c-Bold SDF（派生: NoOutLine / PlusPadding）。「▶」は無いので「→」を使う
- ボタン素材は文字なし → ラベルはTMPの子オブジェクトで付ける
- 演出は UI/UIFx.cs の PanelOpen/Pop を使い、Tweenには SetLink(gameObject)
- 画像生成が必要なら image-gen で「純色グリーンバック(#00FF00)背景・文字なし」で生成し、クロマキーで緑を抜いて透過PNGにする（縁の緑かぶりも除去）
- 新規UIは未配線でもエラーにならない null-safe 実装にする
```

## デザイントーン（Docs/News_UIParts.md より）
| 用途 | 色 |
|---|---|
| 縁 濃茶 | #5A3A24〜#6B4630 |
| パネル面 中茶 | #8B6244〜#A0785A |
| カード面 薄茶 | #C9A882 |
| 紙・内側 明ベージュ | #EFDFC8〜#F5E9D5 |
| 強調・金額 金 | #E8A33D |
| 赤 | #A8332B |

状態色: ステッパー強調 (1, .82, .3) / 需要 <40% 青灰・40〜70% 白・70%以上 オレンジ。HUD空き帯 x126〜722。

## 再利用資産
- 画面プレハブ（Assets/Prefabs/Screens/）: BlackSmith, InfoBroker, Newspaper, TomsShop, CommonCanvas（HUD/Goldボックス）, TurnEndSummary, Hero, Map, Marketing, FortuneTeller, DungeonSupport, EventPopup, End, Setting, ToolShop
- 部品プレハブ（Assets/Prefabs/）: ItemShopSlot, SaveSlotView, PreparationChoiceSlot, NewsArticleCell, RankRow, TrendRow, TurnEndSummaryRowUI, HintSlot, VerticalScrollView, DropDown, DefaultCanvas, ShopMachineSlot, DungeonLevelUpSlot, AdvertiseSlot
- コンポーネント: ItemDetailPanel, PriceChartView, MoneyDisplay, ExchangePanelController
- スプライト（Assets/UI/）: 基本/（日付・ゴールド・メニュー・施設ボタン 0/1差分）, 鍛冶屋・道具屋/（枠・説明ボックス・アイテムボックス・購入・閉じる・±・タブ）, 新聞/, 追加/（ばつ・スクロール）, バズ/BuzzMode, Character/（トコ立ち絵）
- シェーダー: Assets/Shaders/UIBuzzFrame.shader

## 画像生成の手順
1. 既存素材で代用できないか先に確認する
2. image-gen（`mcp__image-gen__generate_image_gpt2` 等）で生成。プロンプトに必ず含める: `solid pure green (#00FF00) background, no text, no letters, no watermark`, 既存トーン（flat cartoon, wooden panel with gold trim 等）
3. Python（PIL/numpy）でクロマキー: HSVで緑域を透過 → 縁のスピル除去 → 余白トリム → 透過PNG
4. Assets/UI/ の適切なフォルダに配置し、Import Settings を Sprite (2D and UI) に
5. 生成画像に文字が混入していたら作り直す

## ブラッシュアップの観点
整列・余白の統一、情報の優先度（金額・数値を目立たせる）、既存画面との色・枠の一貫性、押せる要素のフィードバック（ホバー/押下）、文字の溢れ、1920x1080での見え方
