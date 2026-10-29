# レリック（装備アイテム）Unityエディタ配線手順

feature/relics ブランチで実装した「レリック（ローグライトの装備ビルド）骨組み」のエディタ手作業手順。
**未配線でもコンパイル・起動し、レリック効果自体は即有効**（3択パネル未配線時は自動獲得にフォールバック）。

## 仕様の要点

- レリック = ラン中限定のパッシブ装備。装備枠は**無制限**（`GameConst.relicSettings.maxEquipSlots`=0。後からデータで絞れる）。
- 効果は2軸のみで拡張する規律:
  - **① Modifier（常時パッシブ）**: `RelicStatId` + Add/Mul + 値のデータ定義だけで完結。計算式 (base+ΣAdd)×ΠMul
  - **② Hook（フェーズフック）**: `behaviourKey` で C#実装（`RelicBehaviourRegistry`）を参照。組み込み例 `dailyGold`
- v1で配線済みの StatId: `ShopRevenueMul`（営業売上倍率）/ `DemandFloorAdd`（需要下限）/
  `DebtAmountMul`（借金返済額。表示と支払いで同一補正）/ `DisplayKindsAdd`（同時陳列銘柄数+N）。
  enum には将来用（ProcurementCostMul / BuzzChanceAdd / DividendMul）も予約済みだが未配線。
- 入手経路: **イベント報酬**（コマンド `GrantRelic` relicId= / `GrantRandomRelic`）+ **配信勝利の3択**
  （BattleResultHandler が保留→ホーム復帰時に3択UI）。準備画面スターターは Phase 6（メタ進行）で追加。
- レア度 Common/Rare/Epic の重み抽選（GameConst で調整可）・重複所持不可・呪いフラグ（isCurse=ふだんの抽選から除外、イベント付与。村の冒険者ギルド Lv5 では配信報酬の最後の1枠に出る: `RelicRewardService.PickCurseChoice`）。
- セーブ: `slot_N/relics.json`（獲得順+汎用カウンタ+選ぶ前の3択 `pendingChoices`）。ニューゲームで消滅。
  3択の保留も保存するので、選ぶ前に終了しても続きからで同じ3択が出る（RelicRewardService.PendingChoices の名前と型は AutoPlay が使うので変えない）。
- 配信: `balance.json` の `relics` リスト区画（relicIdキー）。数値だけ上書き可能。
- 現状は店経営側のみ有効（配信＝FightScene側の効果は今後のフェーズで BattleLifetimeScope にモデル登録して拡張）。

## 1. レリックのマスターデータ作成（必須）

`Assets/Resources_moved/` に Create > ScriptableObjects > Relic > **RelicDefinition** を作成し、
**ラベル `RelicData`** を付与。効果はユーザーが別途企画予定のため、動作検証用の例:

| relicId | 名前案 | rarity | 効果（modifiers/behaviours） |
|---|---|---|---|
| relic_signboard | 手描きの看板 | Common | ShopRevenueMul, Mul, 1.10 |
| relic_showcase | 折りたたみ陳列棚 | Common | DisplayKindsAdd, Add, 1 |
| relic_charm | 商売繁盛のお守り | Rare | DemandFloorAdd, Add, 0.08 |
| relic_seal | ギルドの減免状 | Epic | DebtAmountMul, Mul, 0.8 |
| relic_piggy | 子豚の貯金箱 | Common | behaviours: dailyGold, param=3000（2026-10-28 に 100 → 3000） |
| relic_cursed_ledger | 呪われた帳簿 | Rare(isCurse) | ShopRevenueMul 1.3 + behaviours: ledgerMissPenalty, param=0.2（配信が外れた＝勇者が負けて配当も無い・配信終了ボタンでの途中終了なら、次の朝に所持金の20%を取り立てる。不具合での途中終了は数えない。2026-10-21 に納付金 ×1.2 から変更） |
| relic_allin_dice | 命知らずのサイコロ | Rare(isCurse) | SellClampAdd +0.35（売り注文の約定が注文時の値段から ±55% まで動く。ふだんは ±20%）+ DebtAmountMul 1.1 |

## 2. イベントからの付与（CSV運用）

`EventDatas.csv` のコマンドに以下を追加できる:
- `GrantRelic` … パラメータ `relicId=<id>`（呪い付与にも使う）
- `GrantRandomRelic` … パラメータなし（レア度重み抽選）

## 3. 配信勝利の3択パネル（TomsShopシーン）

1. パネル `RelicChoicePanel`（初期非アクティブ、全画面の半透明黒）は配置済み。構成:
   `Window`（7メッセージボックス）> `Title`「レリックを1つ選ぶ」/ `Cards` > `Choice0〜2`（`Assets/Prefabs/RelicChoiceSlot.prefab`）/ `SkipButton`（文字なし0−0 + TMPラベル）。
   - カード（`RelicChoiceSlotView`）: 6枠の地 + アイコン枠（8−0/8−1）+ 名前 + 説明ボックス（10説明ボックス）。
     アイコンは `RelicDefinition.icon`。レア/エピック/呪いは名前色とアイコン背景にレア度色をうっすら乗せる（コモンは無色）。
     ホバー/パッド選択で金枠（Prep_GoldFrame）+ 少し拡大。説明文は折り返し＋自動縮小（16〜28pt）＋省略。
   - 表示時は `UIFx.PanelOpen`（フェード）+ `Window` を `UIFx.Pop`。辞退ボタンの文言は Presenter が「辞退して ◯◯G もらう」を差し込む。
2. `TomsShopView` の Inspector →「レリック獲得3択」（配線済み）:
   **Relic Choice Panel / Relic Choice Buttons(3) / Relic Choice Name Texts(3) / Relic Choice Desc Texts(3) / Relic Choice Skip Button / Relic Choice Slots(3) / Relic Choice Window**。
   ※ Slots 未配線なら名前/説明テキストだけ更新（アイコン無し）。パネル未配線の間は勝利報酬が先頭候補の自動獲得になる（Consoleに表示）。
   ※ `relicChoicePanel` / `relicChoiceButtons` は DebugAutoPlayer がリフレクション名で参照しているのでリネーム不可。

## 4. 所持レリックバー（任意）

ホーム画面に TextMeshProUGUI を置き、`TomsShopView` → **Relic Bar Text** に割り当て
（「レリック: 手描きの看板 / お守り」のように所持名を並べる簡易表示）。

## 5. 動作確認チェックリスト

1. F11 →「ランダム獲得」→ 所持数+1、`relics.json` 生成。
2. ShopRevenueMul レリック所持 → 営業サマリーの約定入金時、Console [SalesCalculator] に「× レリック」相当の倍率反映。
3. DisplayKindsAdd レリック → 陳列カウンタの分母が+1。
4. DebtAmountMul レリック → ホームの次回返済表示と返済パネルの額が同じく軽減される。
5. dailyGold レリック → 翌朝の朝レポートに「子豚の貯金箱: +3,000G」。
6. F11 →「3択テスト」→ ホームに戻ると3択パネル（未配線なら自動獲得ログ）。
7. 配信勝利 → 帰還後に3択。スキップも可能。
8. セーブ→続きから→所持レリック復元。ニューゲームで消滅。
