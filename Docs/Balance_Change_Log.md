# バランス値の変更ログ

スプレッドシート（TokoTomSettings）と配信（Firebase の config/production）で変えた値の記録。新しいものを下に足す。
「アップロード」は GAS の `apiUploadGameConst`（data シート → gameconst.json）／`apiUploadBalance`（bal_* と villageFacilities → balance.json）。
本番の設定は体験版も読むので、ここに書いた値はすべてのプレイヤーに届く。

## 目標（2026-10-29 時点）

- ゲームの内容を理解している普通のプレイヤー: 破産 1〜2 割
- 上手いプレイヤー: ほぼ破産しない（0〜5%）
- 測り方: 人格 AI の学習マラソン（Medium、15〜20 日、納付 2 回）。集計は `D:\WorkTree\TomsLands\_scratch_wt04\tuning\lap_stats.py`

## 2026-10-28

| シート | キー | 前 | 後 | 理由 |
|---|---|---|---|---|
| data | village.conversionRate | 12.5 | 1 | 村資金が貯まりすぎ（全施設 Lv5 まで約 1.5 時間） |
| data | village.difficultyConversionMultipliers | （なし） | 1,1.25,1.5 | 難易度ごとの換金倍率 |
| data | village.clearBaseFlat | （なし） | 300000 | 完走報酬の土台 |
| data | village.clearDebtMultiplier | （なし） | 2 | 完走報酬 = 土台 + 納付金の予定額 × この値 |
| （アセット） | relic_piggy の dailyGold | 100 | 3000 | 子豚の貯金箱が少なすぎた |

## 2026-10-29

| 時刻 | シート | キー | 前 | 後 | 理由 |
|---|---|---|---|---|---|
| 09:19 | data | debtBaseAmount / debtMultiplier | 15000 / 1.5 | 150000 / 2 | 62 周で破産 1 回。簡単すぎた（試験） |
| 09:50 | data | debtBaseAmount / debtMultiplier | 150000 / 2 | 15000 / 1.5 | 配信熱の複利を直すと稼ぎが大きく減るので、いったん戻して測り直す |
| 09:50 | data | mapInfoBaseCost | （なし） | 10000 | 地図の値段をシートから変えられるようにした（C#） |
| 09:50 | data | mapInfoDebtMultiplier | （なし） | 2 | 地図の値段が納付の期日ごとに上がる |
| 10:20 | bal_battlePrice | restockPriceRate | （なし） | 0.75 | 配信中の補充を「その時の配信の値段 × この値」に（元値で補充して高く売れた） |
| 10:20 | data | flowGeneration.difficultyBias | 0.5（既定） | 2 | 1 本目の配信が一番難しかった（勝率 38%）。序盤はやさしいダンジョンを出やすく |
| 10:20 | data | village.tavernNewspaperDiscounts | 0,0,0.25,0.5,0.65,0.65（既定） | 0,0.25,0.4,0.55,0.65,0.65 | 序盤の施設の効果が薄い（1 段前倒し） |
| 10:20 | data | village.tavernFundFeeReductions | 0,0,0,0.01,0.02,0.02（既定） | 0,0.005,0.01,0.015,0.02,0.02 | 同上 |
| 10:20 | data | village.artisanCostDiscounts | 0,0,0.15,0.15,0.25,0.25（既定） | 0,0.1,0.15,0.2,0.25,0.25 | 同上 |
| 10:20 | data | village.artisanMachineSlotAdds | 0,0,0,1,2,2（既定） | 0,0,1,1,2,2 | 同上 |
| 10:20 | villageFacilities | 酒場 Lv1〜3・職人組合 Lv1〜3 の effectText | | 上の値に合わせた説明 | 表示を効果に合わせる |
| 10:20 | bal_events | 010〜018（損をするイベント）の金額 | -3,000〜-10,000 | -15,000〜-30,000 | 得するイベント（最大 +100,000）に比べて損が小さすぎた |
| 11:00 | bal_battlePrice | heroDefeatRefundRate | （なし） | 0.4 | 勇者が負けた配信は武器・防具の売上の 4 割を返金（返金騒ぎ。C#） |
| 13:30 | data | debtBaseAmount | 15000 | 76000 | 配信熱などを直した後の稼ぎで破産 4〜5 割を狙った（使われる前に差し替え） |
| 13:30 | data | village.clearDebtMultiplier | 2 | 0.5 | 納付金を上げると完走報酬が儲けより大きくなりすぎるため |
| 13:40 | data | debtBaseAmount | 76000 | 32000 | 目標を「理解している普通のプレイヤーで 1〜2 割」に変更（Medium の納付計 8 万 G） |

### 同じ日に C# で変えたこと（値ではないが、バランスに効くもの）

- 配信熱の倍率を毎ターン掛け直さず、段階が変わったときだけ差を掛ける（値段が指数的に上がり続けていた）
- 勇者が負けたら装備の相場が半値になる罰をやめ、返金騒ぎに置き換えた
- 人格 AI がスターターレリックを自分で選ぶ（それまでは常に子豚の貯金箱）
