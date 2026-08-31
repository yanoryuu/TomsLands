# マシン設置（店カスタマイズ）+ 朝レポート Unityエディタ配線手順

feature/shop-machines ブランチで実装した「マシン設置」と「朝レポート」のエディタ手作業手順。
**未配線でもコンパイル・起動する**（マシンショップ画面と朝レポートパネルが出ないだけ。
朝レポートは Console ログにフォールバック）。

## 仕様の要点

- お金でマシンを購入して店に設置。設置枠は**店レベル**（`ShopLevelSettings.machineSlots`: Lv1=1→Lv5=6）。
- 同一マシンは各1台まで。撤去は購入額の50%返金。ラン内リセット。
- 効果は2系統:
  - **毎日発動型**（翌朝発動・ユーザー決定）: お金製造機（+固定G/朝）、アイテム自動生成機（在庫上限超過分は破棄して明示）
  - **常時バフ型**: 客寄せ（営業売上倍率+x%）、需要下限の底上げ
- **朝レポート**: 日送り中の入金イベント（売り注文の持ち越し精算・配当・債券償還・設備収入・生成アイテム）を
  1つの通知に統合して朝に表示（朝の演出渋滞対策）。
- セーブ: `slot_N/shopMachineData.json`。配信: `balance.json` の `shopMachines` リスト区画（machineIdキー）。

## 1. マシンのマスターデータ作成（必須）

`Assets/Resources_moved/` に Create > ScriptableObjects > ShopMachine > **ShopMachineData** を作成し、
**ラベル `ShopMachineData`** を付与。回収4〜6ターン基準の推奨初期ラインナップ:

| machineId | 効果 | コスト | 数値目安 | requiredShopLevel |
|---|---|---|---|---|
| machine_money_s | DailyMoney | 4,000G | +800G/朝（5ターン回収） | 1 |
| machine_money_l | DailyMoney | 12,000G | +2,200G/朝 | 3 |
| machine_maker | DailyItem | 6,000G | 安価な武器×2/朝 | 2 |
| machine_attract | RevenueMultiplier | 8,000G | 売上+10% | 2 |
| machine_fridge | DemandFloorBonus | 5,000G | 需要下限+8% | 1 |

## 2. マシンショップ画面

1. `ShopMachineSlot.prefab` を新規作成（`AdvertiseSlot.prefab` 複製ベース）:
   ルートに `ShopMachineSlotUI`。icon / name / effect / cost / state / selectButton を割り当て。
2. TomsShop シーンに `MachineShopPanel` を作成（広告画面と同構成: 左カタログScrollView+右詳細）:
   ルートに `ShopMachineView`。catalogParent / machineSlotPrefab / slotCounterText / 詳細各種 /
   purchaseButton / removeButton / messageText / closeButton を割り当て。
3. `GamePanelManager` → **Machine Shop Panel** に割り当て。
4. `GameLifetimeScope` → **Shop Machine View** に割り当て（未配線の間はPresenter未登録=安全）。
5. `TurnPhaseView` の `procurementGroup` に「店の設備」ボタンを追加し、
   `TomsShopView` → **Machine Shop Button** に割り当て。

## 3. 店内の見た目反映

1. `TomsShop.prefab` 内（ShopDeskDisplay の兄弟）に `MachineDisplay` GameObject を作成し
   `ShopMachineDisplay` を付ける。
2. 子に SpriteRenderer を最大6個手置き（机や壁際など設置場所らしい位置）し、
   **Machine Slots** 配列に登録。
3. `TomsShopView` → **Shop Machine Display** に割り当て。

## 4. 朝レポートパネル

1. TomsShop シーンにシンプルなポップアップパネル `MorningReportPanel` を作成
   （タイトル「朝の報告」+ 本文 TextMeshProUGUI + 閉じるボタン。初期状態は非アクティブ）。
2. `TomsShopView` → **Morning Report Panel / Morning Report Text / Morning Report Close Button** に割り当て。

## 5. 動作確認チェックリスト

1. マシンショップでお金製造機を購入 → 所持金減・カタログに「設置中」・設置枠カウンタ増加。
2. 翌朝: 朝レポート（またはConsoleの [MorningReport]）に「お金製造機: +800G」が出て入金される。
3. アイテム生成機: 翌朝在庫が増える。在庫満杯なら「破棄」表記。
4. 客寄せマシン設置後の営業: Console の [SalesCalculator] に「× マシン=1.10」が出る。
5. 設置枠がいっぱいのとき購入不可 → 店の改装で枠が増えると購入可能に。
6. 撤去 → 50%返金され枠が空く。
7. セーブ→続きから→設置状態が復元。ニューゲームで全撤去。
8. 配当付き武器・債券償還・売り注文の持ち越し精算も朝レポートに載る（Phase 3 のログ表示から昇格）。
