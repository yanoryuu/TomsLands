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
- ターン評価の「売り切れ率」軸は「資金効率」に差し替え済み。取得単価の分かる約定があれば実現損益の利回り（+10%で満点・0%で半分・赤字で0）、無ければ当日の仕入れ支出に対する売り注文見込み額（F-3）。
- 取得単価（F-1）: 在庫ごとに平均取得単価（移動平均）を持つ（`RuntimeItemData.AverageCost`、セーブ `itemData` の `averageCost`、`sellOrderData` の `unitCost`）。
  仕入れ（手動・オート）は実効単価、製造機の生成は 0G。**古いセーブは -1（不明）**として読み込み、不明な在庫が残っているうちは混ぜない（在庫が 0 になってから仕入れ直すと数え直す）。
  配信中の補充（BattleSceneStarter）は記録していない（取得単価はそのまま）。
- 営業の流れ（F-7）: 「営業開始」→ 精算を先に1回だけ行う（`TurnEndSummaryPresenter.ProcessTodayIfNeeded`。`lastProcessedTurn` のガードはそのまま）→
  店の画面のまま客が机へ来て買っていく営業ダイジェスト（`SalesDigestView`、6人まで・クリック/キー/パッドで飛ばせる・「演出を速くする」で上限半分）→ サマリー。
  ダイジェストが未配線・見せる品が無い・自動プレイ中は従来の暗転。

## 1. 所持金バッジ（CommonView）

1. TomsShop シーンの Common UI（所持金テキスト `playerMoneyText` のある場所）を開く。
2. 所持金テキストの隣に TextMeshProUGUI を新規作成（名前例: `PendingIncomeText`）。
   - 推奨: 小さめフォント・金色系。文言はコードが入れる（「入金予定 +N G」）。
3. `CommonView` の Inspector → **Pending Income Text** に割り当てる。
   - 未約定注文が 0 のときはコードが自動で非表示にする。

## 2. 営業サマリー（TurnEndSummaryView）

**2026-10（F-2）で主役を「本日の入金」にした。** 左列の3行を「本日の入金（金色・数え上げ）／明日の夜に入金／次の配信まで」にし、
`incomeValueText` / `pendingValueText` / `settledIncomeLabel` / `pendingIncomeLabel` / `settlementDetailText` / `daysUntilBattleLabel` / `daysUntilBattleRow` を
`TurnEndSummary.prefab` で配線済み。これらが配線されていると、シーン側で足した旧 `SettledIncomeText` / `PendingIncomeText` は実行時に隠す（シーンは触らない）。
注文だけで入金の無い日（1日目）は「本日の注文 +N G／入金 明日の夜」。明細（`SettlementDetail`）は値動きの大きい2銘柄・倍率の内訳（バズ・フォロワー・設備・レリック）・実現損益。
1周目で占い師に出会う前は「次の配信まで」の行を出さない。最後の配信の前は「最後の配信まで」。以下は旧手順（参考）。

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
5. 借金返済日: 前日の売り注文（約定予定＝今日）は通常は夜の営業サマリーで入金されるが、返済の判定は朝に行う。
   現金が返済額に足りないときだけ、判定の前に今日入金予定の注文を前倒しで約定・入金する
   （`GameFlowManager.SettleTodaySellOrdersForDebt`。朝レポートに「返済のため本日分の売り注文を前倒しで入金」と出る。夜の精算は残りの注文だけ）。
   それでも足りなければ金融資産の強制売却、それでも足りなければ破産。
6. 旧セーブ（sellOrderData.json なし）を続きから → エラーなく注文ゼロで開始できること。

## 5. HUD・朝の通知（2026-10, G-3 / F-4 / F-5 / K-11）

- `CommonCanvas.prefab` の `Date` に `StreamInfoText` を追加（`CommonView.streamInfoText`）。「明日は配信（2/4）」「今日は最後の配信（4/4）」「次の配信まで あと3日（1/4）」。
  1周目で占い師に出会う前は出さない。日数は `GameFlowManager.GetDaysUntilNextStream()`、何回目かは `GetStreamProgress`。
- 所持金は増えると金色・減ると赤に一瞬染め、下に「+1,200G」「-800G」を浮かせる。増減が所持金の30%を超えるとカウントを0.9秒に伸ばす。
- 配信の前日に「営業開始」を押すと一度だけ「明日は配信です（勇者の装備はこれでいいですか？）」を出す（`TomsShopPresenter.StreamEveConfirmTitle`）。
  「勇者を見る」で勇者画面へ。配信の当日は装備を変えられない（寄り道は鍛冶屋だけ）。最後の配信なら「明日は最後の配信です」と決算の一文。
- 朝の通知（保留イベント → 税金 → ターン/バズ演出 → 朝レポート → レリック3択）は**表示の順番はそのまま**、前のもの（と会話）が閉じるまで次を出さない。
  朝レポートの先頭に「今朝の入金 +N G」（日送り前後の所持金の差）と「総資産（昨日比）」。総資産 = 現金 + 在庫の評価額 + 未約定の売り注文 + 金融資産（昨日比は同じシーンの間だけ）。
  朝の入金は HUD の所持金を前日の額に据え置き、朝レポートを閉じたときに数え上げる。
- バズ発生の告知の説明文は「今夜の入金 ×2.5（続けば あと最大3日）」（倍率は約定日＝今夜のサマリーに掛かる）。
- レリック3択のカードに「目安 1日 +1,340G（残り12日 約16,080G）」などを出す（営業売上・返済額・配当の倍率だけ。出せない種類は出さない）。
- 営業ダイジェストの入金時、営業売上に効いたレリックのアイコンを跳ねさせ、そのレリックで増えた額を浮かせる。
