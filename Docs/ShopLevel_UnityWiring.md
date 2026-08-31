# 店レベル（陳列上限・店の改装）Unityエディタ配線手順

feature/shop-level ブランチで実装した「店レベル」システムのエディタ手作業手順。
**未配線でもコンパイル・起動する**（改装画面が出ない/カウンタ非表示なだけ。陳列上限自体は即有効）。

## 仕様の要点

- 店レベル(Lv1〜5)が「同時に陳列できる銘柄数」「1銘柄あたりの陳列個数」「マシン設置枠(将来用)」を規定。
- テーブルは新規SO `ShopLevelSettings`（既定: Lv1=3銘柄/5個 → Lv5=12銘柄/24個、費用4,000〜45,000G）。
- **Lv1=3銘柄は意図的なナーフ**（従来は無制限）。「枠が増えて嬉しい」体験の核。
- レベルアップはゴールド購入（鍛冶屋と同方式）。ラン内完結（ニューゲームでLv1）。
- 鍛冶屋レベル（何を仕入れられるか）とは役割分離。
- セーブは `tomsData.json` の `shopLevel`（旧セーブは欠損→Lv1に正規化）。
- リモート配信: `balance.json` の `shopLevel` 区画で上書き可能。
- デバッグメニュー(F12)の「経済」に店Lv操作ボタンを追加済み（UI未配線でも検証可能）。

## 1. ShopLevelSettings アセットの作成（必須）

1. Project ウィンドウで `Assets/Resources_moved/` を開く。
2. 右クリック → Create > ScriptableObjects > **ShopLevelSettings** → 名前 `ShopLevelSettings`。
3. Addressables に登録し、アドレスを **`ShopLevelSettings`** にする（`ShopEconomySettings` と同じ方式）。
   ※ 未登録でも起動するが、毎回デフォルト値になる警告が出る。

## 2. 店の改装画面（ShopUpgrade）

1. `Assets/Prefabs/Screens/` の他画面(広告画面など)を参考に、TomsShop シーンに `ShopUpgradePanel` を作成。
   - 推奨要素: レベル表示 / 同時陳列プレビュー / 陳列個数プレビュー / 設置枠プレビュー / 費用 / 改装ボタン / 閉じるボタン / メッセージ欄
2. パネルのルートに `ShopUpgradeView` コンポーネントを付け、各 TextMeshProUGUI / Button を割り当てる。
3. `GamePanelManager` の Inspector → **Shop Upgrade Panel** にパネルを割り当てる。
4. `GameLifetimeScope` の Inspector → **Shop Upgrade View** に View を割り当てる。
   ※ ここが未配線の間は Presenter が登録されず、改装画面は開けない（安全設計）。
5. `TurnPhaseView` の `procurementGroup` 配下に「店の改装」ボタンを追加し、
   `TomsShopView` の Inspector → **Shop Upgrade Button** に割り当てる。

## 3. 陳列画面のカウンタ

1. 陳列パネル（ItemSelection）に TextMeshProUGUI を追加（名前例: `SlotCounterText`）。
2. `ItemSelectionView` の Inspector → **Slot Counter Text** に割り当てる。
   - 表示は「陳列 3/5」。上限到達でオレンジ色になる。

## 4. 動作確認チェックリスト

1. 新規ゲーム → 陳列画面で4銘柄目をONにしようとすると弾かれる（Lv1=3銘柄）。
2. おすすめ陳列 → 3銘柄だけ選ばれ、各銘柄の陳列個数が5個以下。
3. F12 デバッグメニュー → 店Lv+1 → 陳列画面で5銘柄までONにできる。
4. 改装画面で費用を払うとレベルが上がり、所持金が減る。資金不足時はボタンが無効。
5. セーブ → 続きから → 店レベルが復元される。旧セーブはLv1扱い。
6. リザルト/ゲームオーバー後の新規ゲームで Lv1 に戻る。
