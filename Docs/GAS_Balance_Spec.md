# GAS実装仕様書：バランス結合配信（balance.json）

対象: Google Apps Script を実装するAI向け。既存の gameconst / items 配信GASに**追記**する形。
最終更新: 2026-06-19

---

## 0. ゴール
複数の調整データを **1ファイル `balance.json`** に結合してFirebase Storageへ配信する。
- 配信先: `https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/balance.json`
  - 既存と同じバケット。**出力パスと中身だけ** `balance.json` に差し替え（認証・アップロード処理は流用）。
- 既存 `gameconst.json` / `items.json` はそのまま（別エンドポイント）。

## 1. 出力JSON 全体構造
```json
{
  "version": 1,
  "schemaVersion": 1,
  "updatedAt": "2026-06-19T00:00:00.000Z",

  "shopEconomy":  { ...ShopEconomySettings のフィールド... },
  "gameBalance":  { ...GameBalanceData のフィールド... },
  "battlePrice":  { ...BattlePriceSettings のフィールド... },

  "advertisements":     [ { "id": "<advertisementName>", ...変更する数値... } ],
  "buzzEffects":        [ { "id": "Flame|Normal|Big",   ...変更する数値... } ],
  "followerMilestones": [ { "id": "<requiredFollowers>", "requiredFollowers": 100, ... } ],
  "enemies":            [ { "id": "<enemyId>", ...変更する数値... } ],
  "dungeons":           [ { "id": "<DungeonName>", ...変更する数値... } ],
  "events":             [ { "id": "001", "title": "...", "description": "...", "command1": "...", "param1Key1": "...", "param1Value1": "..." } ],

  "heroLevels": [ { "Level":1, "MaxHp":100, "Attack":10, "Defense":5 } ]
}
```

## 2. 厳守ルール（Unity側 JsonUtility / Newtonsoft 解析）
- **数値は必ずJSON数値型**（`"500"` のような文字列は不可）。GASで `Number()` 化する。
- **フィールド名は対象クラスのフィールド名と完全一致**（下表）。
- **省略したフィールドはゲーム既定値（ベイク済みSO）が保持される**（前方互換）。＝**変更したい値だけ書けばよい**。
- 区画自体を省略すれば、その区画は丸ごと既定のまま。
- **`schemaVersion` は必ず 1**（Unity `RemoteBalance.ExpectedSchemaVersion=1`）。不一致だと balance 全体が不採用。
- **リスト区画の各要素には `id`（文字列）を必須**で入れる。`id` は突合キー専用で、ゲーム側のフィールドではない（無視される）。
- **enum値は int で書く**（`elementType`, `requiredAttribute` など）。※ items.json は文字列だったが、balance はフィールド一致のため **int**。`buzzEffects` の `id` だけは enum 名の文字列（"Flame" 等、突合用）。
  - **重要：enemies と dungeons で属性 enum の整数が異なる**（下の §2.1 対応表を必ず参照）。文字列で送ると無視され既定値のままになるので必ず int。

### 2.1 enum 整数対応表（厳守）
**`enemies[].elementType` → enum `ElementType`**（Earth/Wind は無く Wood がある点に注意）
| 名前 | int |
|---|---|
| None | 0 |
| Water | 1 |
| Fire | 2 |
| Wood | 3 |
| Light | 4 |
| Dark | 5 |

**`dungeons[].requiredAttribute` → enum `ItemTypeData.ItemAttribute`**（enemies とは別体系）
| 名前 | int |
|---|---|
| Fire | 0 |
| Water | 1 |
| Earth | 2 |
| Wind | 3 |
| Light | 4 |
| Dark | 5 |

**突合キー（id）に使う enum は「名前文字列」**（int不要）：
- `buzzEffects[].id` = `"Flame"` / `"Normal"` / `"Big"`
- `dungeons[].id` = `DungeonName` 名 = `"MausoleumOblivion" / "ScorchingVolcanoPrison" / "IceMistCave" / "DeepGreenBeastForest" / "AncientMechanicalCastle" / "DemonKingCastle"`

⚠️ 例：敵を火属性にするなら `"elementType": 2`、ダンジョンの必要属性を火にするなら `"requiredAttribute": 0`。**同じ「火」でも数値が違う。**
- `heroLevels` は **PascalCase**（`Level/MaxHp/Attack/Defense`）。配列があれば**全置換**、無ければCSV。

## 3. 区画ごとの仕様

### shopEconomy（単一・全フィールドは `ShopEconomySettings.cs` 準拠）
主なフィールド（float、boolは attentionAffectsLowDemand のみ）:
`highDemandThreshold, highDemandPriceRateMin, highDemandPriceRateMax, lowDemandThreshold, lowDemandPriceRateMin, lowDemandPriceRateMax, normalDemandPriceRateMin, normalDemandPriceRateMax, shopPriceFloorRate, shopPriceCeilingRate, victoryAttributeDemandUp, defeatAttributeDemandDown, displayDemandUp, notDisplayDemandDown, demandFloor, demandCeiling, trustFloorBoost, attentionPriceAmplify, attentionAffectsLowDemand(bool), spreadDemandAmplify, retentionStabilizer, followerWeight, followerScale, trendAmplitude, trendConvergenceRate, trendDriftMax, trendDecayRate`

### gameBalance（単一・`GameBalanceData.cs` 準拠）
例: `statMin, statMax, buzzAttentionCoeff, buzzTrustCoeff, buzzMaxBaseChance, flameTrustThreshold, flameChance, buzzBaseChance, buzzMaxChance, bigBuzzBaseChance, bigBuzzMaxChance, buzzContinueChance, buzzEvolveToBigChance, initialTrust, initialAttention, initialSpread, initialRetention, initialFollowers`（クラスの全フィールド名に一致させる）

バズ確率（2026-08 新方式）: `buzzBaseChance/buzzMaxChance` = 通常バズの基礎/最大発生率(%)、`bigBuzzBaseChance/bigBuzzMaxChance` = 超バズの基礎/最大発生率(%)、`buzzContinueChance` = 毎ターンの継続率(%)、`buzzEvolveToBigChance` = 通常バズ→超バズ発展率(%)。強化度合いは `buzzAttentionCoeff/buzzTrustCoeff/buzzMaxBaseChance`（正規化基準）とフォロワーボーナスから算出される。`bigBuzzTrustThreshold` は廃止（コード・シートとも削除済み。配信JSONに残っていても無視される）。

### battlePrice（単一・`BattlePriceSettings.cs` 準拠）
例: `weaponPriceUpOnHit, weaponPriceDownOnNonKill, armorPriceDownOnHit, armorPriceUpOnBlock, effectiveAttributeRate, weakAttributeRate, priceFloorRate, priceCeilingRate, initialHeat, heatTurnDecay, coldTierMax, normalTierMax, hotTierMax, coldPriceMultiplier, normalPriceMultiplier, hotPriceMultiplier, superHotPriceMultiplier, demandEffectiveAttributeUp, demandWeakAttributeDown, buzzBonus2Turn, buzzBonus3PlusTurn, unsoldPenalty, highPriceThreshold, lowPriceThreshold, highPriceDemandDecay, lowPriceDemandGrowth`

### advertisements[]（部分・`AdvertisementData`）
`id` = advertisementName。変更可: `advertisementName, cost, trustGain, attentionGain, spreadGain, retentionGain, followerGain`。icon/selectedBackground は載せない（SO保持）。

### buzzEffects[]（部分・`BuzzEffectData`）
`id` = `"Flame"|"Normal"|"Big"`（= buzzType の名前）。変更可: `immediateRevenueMultiplierBase, immediateFollowerBase, durationBase, sustainedAllStatGain, afterGrantFreeMarketing(bool)` 等の数値/bool。`buzzType` と `afterFreeMarketingData` は載せない。

### followerMilestones[]（部分・`FollowerMilestoneData`）
`id` = requiredFollowers の文字列。変更可: `requiredFollowers, salesBonusRate, buzzChanceBonus, adDiscountRate`。

### enemies[]（部分・`EnemyData`）
`id` = enemyId。変更可: `enemyName, hp, attackPower, defensePower, elementType(int), description, isBoss(bool)`。enemySprite/skills は載せない。
※ダンジョンに埋め込まれた敵（DungeonLevelData.monsters）は別参照のため、この区画は `EnemyData` ラベルのマスターにのみ反映される。

### dungeons[]（部分・`DungeonInfoScriptableObj`）
`id` = key（`DungeonName` 名、例 `"DemonKingCastle"`）。変更可: `dungeonName, dungeonDescription, initDungeonLevel, recommendedLevel, difficulty, requiredAttribute(int)`。各Sprite・levelDataList は載せない（SO保持）。

### heroLevels[]（全置換・`HeroLevelData`）
`Level, MaxHp, Attack, Defense`（PascalCase）。配列があれば CSV を**完全に置き換える**。

### events[]（全置換・`TomsEvent`、2026-08追加）
店イベントのマスター。配列があれば `EventDatas.csv`（ベイク）を**完全に置き換える**。
列: `id, title, description, command1, param1Key1, param1Value1, command2, param2Key1, param2Value1`（command は最大10個まで `command{n}/param{n}Key1/param{n}Value1` で拡張可）。
- `id`/`title` が空の行は Unity 側で除外される（空行プレースホルダを置いてもよい）。
- 値はすべて**文字列扱い**（Unity側で必要に応じて数値パースする）。数値セルが number 型で出力されても受け側（JObject.Value&lt;string&gt;）で吸収する。
- 現在使われている command: `ChangeMoney`（param: amount=金額±）, `ChangeTrust`（param: amount=信頼±）。
- シート雛形: `Docs/balance_tsv/events.tsv`（現行CSVの実データ21件を変換済み）。

### villageFacilities[]（部分・`VillageFacilityData`、2026-08追加）
村（メタ層）の施設マスター。`id` = facilityId（hall/guild/antique/shrine/bank/warehouse/road/press/artisan/tavern/workshop/farm/training）。
変更可: `facilityName, description, requiredHallLevel, levels[]`（levels の要素: `cost, effectText`。V2以降は `startBonusKey, startBonusValue, unlockRelicTier` も）。
- **シートは1行=1施設×1レベル**（`Docs/balance_tsv/villageFacilities.tsv` 雛形）。GAS側で `id` ごとに `level` 昇順でグループ化し、
  `levels` 配列（レベル列自体は出力しない）を持つ1オブジェクトに変換して出力する:
  `{ "id": "guild", "facilityName": "冒険者ギルド", "requiredHallLevel": 0, "levels": [ { "cost": 4000, "effectText": "..." }, ... ] }`
- `levels` を載せる場合は**その施設の全レベルぶんを載せる**こと（JsonUtilityの配列上書きは全置換のため、部分だけ書くと段数が縮む）。
- 村のスカラー設定（純資産→村資金の変換率など）はこの区画ではなく **gameconst.json の `village` オブジェクト**
  （`conversionRate` / `bankruptcyConversionRate` / `debtScalePerVillageLevel`）で配信する。
- ⚠️ **汎用の行シートリーダー（readListSheet_）では処理できない**（同一idが複数行になり後勝ちで潰れる＋
  `level/cost/effectText` はフラットフィールドとして存在しないため無視される）。
  **専用リーダー `readVillageFacilities_()` で id ごとに levels[] へグループ化する**（§8のコード参照）。
  シート名は `villageFacilities`、ヘッダは型サフィックス不要（`id / facilityName / requiredHallLevel / level / cost / effectText`。
  V2以降は `startBonusKey / startBonusValue / unlockRelicTier` 列を追加可）。

### finance（単一・`FinanceSettings.cs` 準拠、2026-08追加）
金融システムのスカラー設定。シートは **`bal_finance`**（key/value/type 形式・雛形 `Docs/balance_tsv/finance.tsv`）。
フィールド: `fundBuyFeeRate(float), fundSellFeeRate(float), forcedSaleExtraFeeRate(float), bondEarlyRedemptionRate(float), navHistoryCapacity(int)`

### financialProducts[]（部分・`FinancialProductData`、2026-08追加）
金融商品（債券・ファンド）のマスター。`id` = productId。シートは **`bal_financialProducts`・1行=1商品**
（汎用行リーダー対応・ヘッダに `:type` サフィックス必須。雛形 `Docs/balance_tsv/financialProducts.tsv`、現行9商品の実データ変換済み）。
変更可: `productName, description, kind(int), unlockInfoBrokerLevel(int), bondUnitPrice(int), bondInterestRate(float),
bondMaturityTurns(int), fundBaseUnitPrice(int), useAttributeFilter(bool), attribute(int)`。icon は載せない（SO保持）。
- **enum int**: `kind` → FinancialProductKind: Bond=0 / IndexFund=1。`attribute` → `ItemTypeData.ItemAttribute`
  （dungeons と同じ表: Fire=0/Water=1/Earth=2/Wind=3/Light=4/Dark=5）。
- `description` セル内の `\n` はそのまま文字列として渡る（改行変換なし。1行で書くこと）。

### relics — 配信しない（2026-08-31決定）
レリックは **Unity上の ScriptableObject（RelicDefinition）で直接管理**し、スプレッドシートからは配信しない。
Unity側の受け口（`RemoteBalance.ListSections` の `"relics"`）は残っているが、GAS・シートは作らないこと。
（modifiersの入れ子や enum int の管理がシートだと煩雑で、Unityのインスペクタで編集する方が安全なため）

## 4. 完全な例
```json
{
  "version": 3,
  "schemaVersion": 1,
  "updatedAt": "2026-06-19T12:00:00.000Z",
  "shopEconomy": { "shopPriceCeilingRate": 3.5, "trendDriftMax": 0.10 },
  "gameBalance": { "flameChance": 0.05, "initialFollowers": 0 },
  "battlePrice": { "initialHeat": 0.2, "unsoldPenalty": 0.9 },
  "advertisements": [
    { "id": "テレビCM", "cost": 1200, "followerGain": 80 }
  ],
  "buzzEffects": [
    { "id": "Big", "immediateRevenueMultiplierBase": 2.5, "durationBase": 3 }
  ],
  "followerMilestones": [
    { "id": "1000", "requiredFollowers": 1000, "salesBonusRate": 0.1 }
  ],
  "enemies": [
    { "id": "slime", "hp": 60, "attackPower": 12 }
  ],
  "dungeons": [
    { "id": "DemonKingCastle", "difficulty": 9, "recommendedLevel": 25 }
  ],
  "villageFacilities": [
    { "id": "guild", "requiredHallLevel": 0, "levels": [
      { "cost": 4000, "effectText": "レリック Tier1（5種）が報酬の抽選に加わる" },
      { "cost": 10000, "effectText": "レリック Tier2（4種）が加わる" },
      { "cost": 20000, "effectText": "レリック Tier3（4種）が加わる" }
    ] }
  ],
  "heroLevels": [
    { "Level": 1, "MaxHp": 120, "Attack": 12, "Defense": 6 },
    { "Level": 2, "MaxHp": 140, "Attack": 15, "Defense": 7 }
  ],
  "events": [
    { "id": "001", "title": "記念硬貨の発見", "description": "……", "command1": "ChangeMoney", "param1Key1": "amount", "param1Value1": "30000" }
  ]
}
```

## 5. シート構成（推奨）
- 単一区画（shopEconomy/gameBalance/battlePrice）: それぞれ `key/value/type` のフラットシート（gameconstと同形式）。GASがオブジェクト化。
- リスト区画 / heroLevels: 種類ごとに行シート（itemsと同形式）。1行=1要素、列にフィールド。GASが配列化。
- これらを1つの `balance.json` に結合して出力。`version` は配信ごとに+1、`schemaVersion=1`。

## 6. 受け入れ条件
1. 公開URL `.../balance.json` をGETして §1 構造のJSONが返る。
2. 数値が数値型、enumがint、リスト要素に `id` がある。
3. 変更したフィールドだけ書いて配信→ゲームで該当値のみ変化（他はベイク値保持）。
4. `version` を上げて再配信→Unityログ `[RemoteBalance] version N を適用` の N が増える。
5. `schemaVersion` を1以外にすると balance 全体が不採用になる。

## 7. Unity側対応状況（実装済み・GAS担当は不要）
- 取得・分割・適用・キャッシュ・フォールバックは実装済み（`RemoteBalance` / `RemoteBalanceService` / `BootLifetimeScope` / 各ロード箇所）。
- 確認は `Tools > TomsLands > リモート設定 > Balance確認ウィンドウ`。
- GAS担当は本書 §1〜§3 を満たす `balance.json` を配信すればよい。

## 8. GAS追加コード（villageFacilities 専用リーダー・2026-08）

**全文コピペ用のGASは `Docs/gas/GameConstGas.gs`**（本節はその差分説明）。既存GASへの変更は3点:
1. `BALANCE_LISTS` から `villageFacilities` を**削除**（汎用リーダーでは処理不可のため）
2. 定数追加: `const VILLAGE_FACILITIES_SHEET = 'villageFacilities';`
3. `buildBalanceEnvelope()` のリスト区画処理の後に以下を追加し、下記の関数を貼り付ける:

```js
  // villageFacilities（1行=1施設×1レベル → levels[] にグループ化）
  const facilities = readVillageFacilities_();
  if (facilities.length > 0) envelope.villageFacilities = facilities;
```

```js
/**
 * villageFacilities: 1行=1施設×1レベル（ヘッダ: id/facilityName/requiredHallLevel/level/cost/effectText。
 * 任意で startBonusKey/startBonusValue/unlockRelicTier）。型サフィックス不要。
 * id ごとに level 昇順でグループ化し、levels[] を持つ1オブジェクトに変換する。
 * levels は Unity 側で全置換されるため、載せる施設は全レベル行を書くこと。
 */
function readVillageFacilities_() {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(VILLAGE_FACILITIES_SHEET);
  if (!sheet) return [];
  const values = sheet.getDataRange().getValues();
  if (values.length < 2) return [];

  const header = values[0].map(function (h) { return String(h).trim().split(':')[0]; });
  const col = {};
  header.forEach(function (h, i) { if (h && !(h in col)) col[h] = i; });
  ['id', 'level', 'cost', 'effectText'].forEach(function (req) {
    if (!(req in col)) throw new Error(VILLAGE_FACILITIES_SHEET + ': 列 "' + req + '" がありません。');
  });

  const byId = {};
  const order = [];
  for (let r = 1; r < values.length; r++) {
    const row = values[r];
    const id = String(row[col.id]).trim();
    if (id === '') continue;
    const ctx = VILLAGE_FACILITIES_SHEET + '(行' + (r + 1) + ')';

    if (!byId[id]) {
      const obj = { id: id };
      if ('facilityName' in col && String(row[col.facilityName]).trim() !== '')
        obj.facilityName = String(row[col.facilityName]).trim();
      if ('requiredHallLevel' in col && row[col.requiredHallLevel] !== '')
        obj.requiredHallLevel = castCell_(row[col.requiredHallLevel], 'int', ctx + '!requiredHallLevel');
      byId[id] = { obj: obj, rows: [] };
      order.push(id);
    }

    const entry = {
      cost: castCell_(row[col.cost], 'int', ctx + '!cost'),
      effectText: String(row[col.effectText])
    };
    if ('startBonusKey' in col && String(row[col.startBonusKey]).trim() !== '') {
      entry.startBonusKey = String(row[col.startBonusKey]).trim();
      entry.startBonusValue = castCell_(row[col.startBonusValue], 'float', ctx + '!startBonusValue');
    }
    if ('unlockRelicTier' in col && row[col.unlockRelicTier] !== '')
      entry.unlockRelicTier = castCell_(row[col.unlockRelicTier], 'int', ctx + '!unlockRelicTier');

    byId[id].rows.push({ level: castCell_(row[col.level], 'int', ctx + '!level'), entry: entry });
  }

  return order.map(function (id) {
    const g = byId[id];
    g.rows.sort(function (a, b) { return a.level - b.level; });
    g.obj.levels = g.rows.map(function (x) { return x.entry; });
    return g.obj;
  });
}
```

