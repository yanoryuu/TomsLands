# 売り注文（遅延約定）Unityエディタ配線手順

feature/sell-orders ブランチで実装した「売却遅延（陳列=売り注文、翌日全量約定）」の
エディタ手作業手順。**コードは未配線でもコンパイル・動作する**（表示が出ないだけ）。

## 仕様の要点

- 陳列した商品は営業開始時に「売り注文」となり、在庫はその場で引き当て（減少）。
- 注文は**翌日の営業サマリー**で全量約定して入金される（`ShopEconomySettings.sellOrderDelayTurns = 1`）。
- 約定価格 = 約定日の市場価格を注文時価格の ±20% にクランプ（`sellOrderPriceClampRate`）。
- 配信日・イベント日を挟んで営業サマリーが走らなかった注文は、翌朝の日送り時に自動精算される。
- 配信（バトル）の売上は従来通り即金。
- 旧確率販売に戻す場合は `ShopEconomySettings.useProbabilisticShopSales = true`。
- セーブは `slot_N/sellOrderData.json`。旧セーブは注文ゼロとして読み込まれる。
- ターン評価の「売り切れ率」軸は「資金効率」（当日の仕入れ支出に対する売り注文見込み額）に差し替え済み。

## 1. 所持金バッジ（CommonView）

1. TomsShop シーンの Common UI（所持金テキスト `playerMoneyText` のある場所）を開く。
2. 所持金テキストの隣に TextMeshProUGUI を新規作成（名前例: `PendingIncomeText`）。
   - 推奨: 小さめフォント・金色系。文言はコードが入れる（「入金予定 +N G」）。
3. `CommonView` の Inspector → **Pending Income Text** に割り当てる。
   - 未約定注文が 0 のときはコードが自動で非表示にする。

## 2. 営業サマリー（TurnEndSummaryView）

1. TurnEndSummary パネルの prefab / シーンオブジェクトを開く。
2. TextMeshProUGUI を2つ追加:
   - `SettledIncomeText` … 本日の約定入金（「本日の入金 +N G」）
   - `PendingIncomeText` … 本日出した売り注文（「明日入金予定 +N G」）
3. `TurnEndSummaryView` の Inspector → **Settled Income Text** / **Pending Income Text** に割り当てる。
4. 既存の「売上合計」ラベル（`totalRevenueText` の見出し）を **「売却額（明日入金）」** に文言変更する。
   ※ `totalRevenueText` に入る数値は「本日出した売り注文の見込み額」に意味が変わった。

## 3. ShopEconomySettings アセット

`Assets/Resources_moved/ShopEconomySettings.asset` を選択すると
「売り注文（遅延約定）」セクションが増えている。既定値:

| 項目 | 既定値 | 意味 |
|---|---|---|
| sellOrderDelayTurns | 1 | 約定までのターン数 |
| sellOrderPriceClampRate | 0.2 | 約定価格のクランプ幅(±20%) |
| sellOrderFeeRate | 0 | 売却手数料（将来用） |
| useProbabilisticShopSales | false | true で旧確率販売に戻す |

`Docs/balance_tsv/shopEconomy.tsv` を運用している場合は同名フィールドを追記する。

## 4. 動作確認チェックリスト

1. 新規ゲーム → 仕入れ → 陳列 → 営業開始 → サマリーに「明日入金予定 +N G」が出て、**所持金は増えない**こと。
2. 確認 → 翌日: 所持金バッジ「入金予定」が消え、次の営業サマリーの「本日の入金」で入金されること。
3. 翌日が配信日のケース: 配信から帰還した翌朝、Console に「持ち越し売り注文を精算」が出て入金されること。
4. 売り注文がある状態でセーブ → タイトル → 続きから → バッジ金額が復元されること。
5. 借金返済日: 前日の売り注文の入金が返済チェックより先に行われること（返済日は朝の精算後に判定される）。
6. 旧セーブ（sellOrderData.json なし）を続きから → エラーなく注文ゼロで開始できること。
