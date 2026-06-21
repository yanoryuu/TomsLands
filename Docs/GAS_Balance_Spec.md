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
例: `statMin, statMax, buzzAttentionCoeff, buzzTrustCoeff, buzzMaxBaseChance, flameTrustThreshold, flameChance, bigBuzzTrustThreshold, initialTrust, initialAttention, initialSpread, initialRetention, initialFollowers`（クラスの全フィールド名に一致させる）

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
  "heroLevels": [
    { "Level": 1, "MaxHp": 120, "Attack": 12, "Defense": 6 },
    { "Level": 2, "MaxHp": 140, "Attack": 15, "Defense": 7 }
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
- 確認は `Tools > Balance > リモート確認ウィンドウ`。
- GAS担当は本書 §1〜§3 を満たす `balance.json` を配信すればよい。
