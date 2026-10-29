# balance.json current values (for sheet paste)

source: マスターシート（2026-10-21 経済バランス H の変更後。Docs/balance_tsv と同じ）。ステータスの尺度は statMax=1000（Docs/ShopEconomy.md §10.3）

## shopEconomy (ShopEconomySettings)  [sheet: shopEconomy / key-value]
```
key	value
highDemandThreshold	0.7
highDemandPriceRateMin	1.1
highDemandPriceRateMax	1.3
lowDemandThreshold	0.3
lowDemandPriceRateMin	0.7
lowDemandPriceRateMax	0.9
normalDemandPriceRateMin	0.95
normalDemandPriceRateMax	1.05
shopPriceFloorRate	0.3
shopPriceCeilingRate	3
victoryAttributeDemandUp	0.15
defeatAttributeDemandDown	0.15
displayDemandUp	0.03
notDisplayDemandDown	0
demandFloor	0.05
demandCeiling	1
trustFloorBoost	0.4
attentionPriceAmplify	0.5
attentionAffectsLowDemand	FALSE
spreadDemandAmplify	1
retentionStabilizer	0.6
followerWeight	0.1
followerScale	1000
trendAmplitude	0.3
trendConvergenceRate	0.15
trendDriftMax	0.12
trendDecayRate	0.1
useAbmPriceEngine	TRUE
demandPricePremium	0.5
sellOrderDelayTurns	1
sellOrderPriceClampRate	0.2
sellOrderFeeRate	0
useShopCustomerCap	TRUE
customersAtNeutralDemand	32
customerBaseShare	0.2
customerDemandSlope	1.6
customerFollowerWeight	0.25
customerAttentionBoost	0.3
sellPressureStrength	0.04
sellPressureDepth	40
sellPressureMaxDrop	0.15
```

## gameBalance (GameBalanceData)  [sheet: gameBalance / key-value]
```
key	value
statMin	0
statMax	1000
followerMin	0
buzzAttentionCoeff	0.05
buzzTrustCoeff	0.02
buzzMaxBaseChance	50
flameTrustThreshold	500
flameChance	30
bigBuzzTrustThreshold	800
buzzBaseChance	10
buzzMaxChance	20
bigBuzzBaseChance	1
bigBuzzMaxChance	5
buzzContinueChance	50
buzzEvolveToBigChance	20
initialTrust	500
initialAttention	0
initialSpread	0
initialRetention	0
initialFollowers	0
buzzContinueRetentionBonus	35
buzzContinueChanceMax	85
attentionDecayRate	0.1
spreadDecayRate	0.1
retentionDecayRate	0.05
trustDecayRate	0
```

## battlePrice (BattlePriceSettings)  [sheet: battlePrice / key-value]
```
key	value
weaponPriceUpOnHit	1.1
weaponPriceDownOnNonKill	0.95
armorPriceDownOnHit	0.98
armorPriceUpOnBlock	1.05
effectiveAttributeRate	1.1
weakAttributeRate	1
priceFloorRate	0.2
priceCeilingRate	10
initialHeat	30
heatTurnDecay	5
coldTierMax	25
normalTierMax	50
hotTierMax	75
coldPriceMultiplier	0.9
normalPriceMultiplier	1
hotPriceMultiplier	1.1
superHotPriceMultiplier	1.2
demandEffectiveAttributeUp	0.2
demandWeakAttributeDown	0.1
buzzBonus2Turn	0.05
buzzBonus3PlusTurn	0.1
unsoldPenalty	0.08
highPriceThreshold	2
lowPriceThreshold	0.5
highPriceDemandDecay	0.92
lowPriceDemandGrowth	1.08
```

## advertisements (AdvertisementData)  [sheet: advertisements / row=element]
```
id	advertisementName	cost	trustGain	attentionGain	spreadGain	retentionGain	followerGain	statMax
総合マーケティング	総合マーケティング	300000	150	150	150	150	0	0
SNSフォロワーキャンペーン	SNSフォロワーキャンペーン	120000	0	100	0	0	10000	0
インフルエンサー起用	インフルエンサー起用	200000	0	200	400	0	0	0
リピーター施策	リピーター施策	180000	200	0	0	400	0	0
SNS広告	SNS広告	100000	0	400	0	200	0	0
口コミキャンペーン	口コミキャンペーン	150000	400	0	200	0	0	0
```

## buzzEffects (BuzzEffectData)  [sheet: buzzEffects / row=element]
```
id	buzzType	immediateRevenueMultiplierBase	immediateRevenueSpreadCoeff	immediateTrustChange	immediateAttentionChange	immediateFollowerBase	immediateFollowerSpreadCoeff	immediateFollowerFixed	durationBase	durationRetentionDivisor	sustainedAllStatGain	sustainedFollowerGain	sustainedTrustChange	sustainedRevenueMultiplier	sustainedAdDiscountRate	afterTrustChange	afterAttentionChange	afterGrantFreeMarketing	immediateRevenueMultiplierMax
Big	2	2.5	0.001	150	200	0	3	0	3	250	50	300	0	0	0.3	200	0	1	3.5
Flame	0	0.5	0	-300	200	-500	0	1	3	250	0	0	-50	0.5	0	0	-100	0	0
Normal	1	1.5	0.001	50	0	0	1	0	3	250	20	100	0	0	0	100	0	0	2.5
```

## followerMilestones (FollowerMilestoneData)  [sheet: followerMilestones / row=element]
```
id	requiredFollowers	salesBonusRate	buzzChanceBonus	adDiscountRate
1000	1000	0.05	0	0
5000	5000	0.15	5	0
10000	10000	0.3	10	0.1
50000	50000	0.5	15	0.2
```

## dungeons (DungeonInfoScriptableObj scalars only)  [sheet: dungeons / row=element]
```
id	key	dungeonName	dungeonDescription	initDungeonLevel	recommendedLevel	difficulty	requiredAttribute
ScorchingVolcanoPrison	1	灼熱の火山牢	マグマが流れる火山の中に築かれた古代の監獄。今や炎をまとう魔物たちが跋扈する無法地帯と化している。	1	5	5	1
DemonKingCastle	5	魔王城	世界の頂、空の果て。そこが最後の決戦の地、魔王城。城の周囲を覆う邪悪な光が、魔王の強大な力を物語っている。	1	10	10	4
DeepGreenBeastForest	3	深緑の獣林	光が差し込まない鬱蒼とした森。魔獣と木の魔物の巣窟で、\n多くの新人冒険者がこの地を訪れる。	1	1	1	0
IceMistCave	2	氷霧の洞窟	雪と氷に閉ざされた洞窟。視界の悪さに加え、肌を刺すような寒さが冒険者を襲う。滑りやすい氷の床にも注意が必要だ。	1	3	3	2
MausoleumOblivion	0	忘却の霊廟	かつての偉大なる王が眠る巨大な地下墓地。闇に魅入られた死者たちがさまよい、訪れしものたちの精神を蝕む。	1	7	7	4
AncientMechanicalCastle	4	古代機構城メタリオン	魔王軍の技術によって建造された鋼鉄の城塞。無数の歯車と機関が絶えず稼働し、中に潜むモンスターたちは侵入者をすべて排除する。	1	8	9	5
```

## heroLevels (HeroStatusData.csv / full replace - write all rows)
```
Lv,MaxHp,Attack,Defense
1,300,55,10
2,430,70,13
3,580,90,16
4,760,115,19
5,980,145,23
6,1220,180,26
7,1480,220,29
8,1780,265,32
9,2120,315,35
10,2500,370,38
```

## notes
- enums are int. (buzzEffects.id and dungeons.id are name strings)
- dungeons.requiredAttribute = ItemAttribute(Fire=0,Water=1,Wood=2,Light=4,Dark=5; 3 unused)
- enemies excluded (dungeon-embedded enemies are not covered by current override path)
## data（gameconst.json の data。H で変えた行だけ。納付金は 2026-10-29 に 15000・1.5 → 150000・2 に上げてテスト中：破産率の目標 ふつう 4〜5 割・熟練 1〜2 割）
```
key	value	type
debtBaseAmount	150000	int
debtMultiplier	2	float
debtFireSaleRate	0.6	float
heroAttributeAttackBonus	1	float
```

## finance（追加）
```
bondUnitCapPerInfoLevel	10	int
```
債券の利率: 短期 0.06（5日）/ 長期 0.18（12日）。貯金ゴーレム（同梱のみ）: 5,600 / 9,100 / 15,400G。
