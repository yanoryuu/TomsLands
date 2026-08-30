# 金融商品（配当付き武器・債券・ファンド）Unityエディタ配線手順

feature/finance ブランチで実装した金融システムのエディタ手作業手順。
**未配線でもコンパイル・起動する**（取引所UIが表示されないだけ。配当・償還のロジックは即有効）。

> **2026-08 更新2（取引所を情報屋へ移設）**
> - 取引所は**情報屋画面のタブ**になった（地図タブの隣・InfoBrokerTab.Exchange）。
>   鍛冶屋のSpecialタブは非表示化し、BlackSmith側の金融コードは削除。
>   売買ロジックは `ExchangePanelController`（Finance/）に共通化し InfoBrokerPresenter が使う
> - 数量指定に**スライダー**を追加（上限=買える口数と保有口数の大きい方）。
>   説明文は折り返し+自動縮小+省略ではみ出さない
> - 配線: InfoBrokerView の exchangeTab / exchangeButton / exchangePanel / exchangeListParent /
>   itemShopSlotPrefab / financeDetailPanel（すべて配線済み）

> **2026-08 更新（金融商品と武具の同列UI化）**
> - 取引所のリストは専用の FinancePanel/FinanceSlot を廃止し、武具タブと**同じカタログリスト・
>   同じ行プレハブ（`ItemShopSlot.SetFinance`）**を共用する（FinanceSlot.cs / FinanceSlot.prefab は削除済み）
> - 行の表示: 価格+前日比矢印 / 市況欄=債券は利率・ファンドは前日比% / 保有欄=口数。未解禁は行を薄表示
> - 右の詳細は `FinanceDetailPanel` のまま、ItemDetailPanel と同位置・同素材（6枠_0等）に統一済み
> - Special タブはシーン上でアクティブ化し「取引所」ラベルを追加済み。配線は BlackSmithView の
>   **Finance Detail Panel** 1参照のみ（下記 §2 の旧手順は履歴として残す）

## 仕様の要点

- **配当付き武器**: `ItemData.dividendPerTurn` > 0 の武器は、在庫1個につき毎朝その額が入金される。
  「売って儲ける」vs「持ち続けて配当」のジレンマ。GASシート運用時は items シートに `dividendPerTurn` 列を追加。
- **債券（ギルド債）**: 購入で資金ロック → 満期ターンの朝に元本+利息で償還。10ターンごとの借金返済との
  タイミング管理が緊張を生む。
- **ファンド**: 買っても在庫は増えず口数を保有。基準価額 = fundBaseUnitPrice × 解放済み構成銘柄の
  (現在価格/基準価格) 平均。属性別6本+全銘柄(市場指数)を想定。いつでも解約可（手数料2%）。
- **解放**: 情報屋レベル（`unlockInfoBrokerLevel`）でゲート。
- **破産判定は現金のみ**。ただし強制返済時に不足していて金融資産で届く場合は、自動で強制売却
  （ファンド=手数料割増 / 債券=元本85%・利息なし）して救済する。
- UI は仕入れ画面の **Special タブ**（今まで押しても何も起きなかったタブ）を「取引所」として使う。
- セーブ: `slot_N/portfolioData.json`。旧セーブはポジションゼロ。
- 配信: `balance.json` の `finance`(単一SO) / `financialProducts`(リスト・productIdキー) 区画。

## 1. アセット作成（必須）

1. `Assets/Resources_moved/` に Create > ScriptableObjects > Finance > **FinanceSettings** を作成。
   Addressables 登録（アドレス `FinanceSettings`）。
2. 同様に **FinancialProductData** を商品ぶん作成し、**ラベル `FinancialProductData`** を付与。推奨初期ラインナップ:

| productId | 種別 | 内容 | unlockInfoBrokerLevel |
|---|---|---|---|
| bond_short | Bond | 額面2,000G / 利率10% / 5日満期 | 1 |
| bond_long | Bond | 額面5,000G / 利率25% / 12日満期 | 2 |
| fund_market | IndexFund | 全銘柄（市場指数）/ 基準1,000G | 1 |
| fund_fire 等 | IndexFund | 属性別（useAttributeFilter=ON）/ 基準1,000G | 2〜3 |

3. 配当付き武器: 既存の `ItemData` アセットのうち数点（例: 高レベル武器）の **dividendPerTurn** に
   10〜50G 程度を設定（basePrice の 1〜2%/日 目安。回収50〜100日相当で売却と拮抗させる）。

## 2. 取引所UI（BlackSmith の Special タブ）

1. `FinanceSlot.prefab` を新規作成（`ItemShopSlot.prefab` 複製ベース）:
   - ルートに `FinanceSlot` コンポーネント。icon / name / price / info / holdings / selectButton / lockedOverlay / selectionHighlight を割り当て。
2. BlackSmith 画面（左リスト+右詳細の構造）内に `FinancePanel` を新規作成:
   - 左: ScrollView + Content（`financeContent`）
   - 右: `FinanceDetailPanel` コンポーネント付きパネル（icon/name/description/unitPrice/detail/holdings、
     `PriceChartView`（基準価額チャート）、数量 +/- ボタン、合計、買うボタン、売るボタン）
3. `BlackSmithView` の Inspector →「取引所（Special タブ）」の4参照
   （Finance Panel / Finance Content / Finance Slot Prefab / Finance Detail Panel）を割り当てる。
4. Special タブのボタン表記を「特別」→「取引所」等に変更（任意）。
5. `ItemDetailPanel` に配当表示用 TextMeshProUGUI を追加し **Dividend Text** に割り当て（任意）。

## 3. 動作確認チェックリスト

1. F12 デバッグメニュー →「最初の商品を1口買う」→ 所持金が減りポジション件数が増える。
2. 債券購入 → 満期ターンの朝、Console に「債券償還」ログ+入金。
3. dividendPerTurn を設定した武器を仕入れる → 毎朝「配当収入」ログ+入金。売却すると止まる。
4. 取引所タブ: 商品一覧表示 / 未解放はロック表示 / 情報屋Lvを上げると解放。
5. ファンド購入 → 数ターン後に基準価額チャートが動く → 解約で現金化。
6. 借金の強制返済日に現金不足+ファンド保有 → 自動売却されて返済パネルが出る（Console「強制売却」）。
7. セーブ → 続きから → ポジションとチャート履歴が復元される。

## 備考

- 配当・償還の入金は現状 Console ログのみ（朝レポートUIは Phase 4 マシン設置で統合予定）。
- ターン終了サマリーの「本業/金融」2段表示も朝レポートと合わせて Phase 4 で対応。
