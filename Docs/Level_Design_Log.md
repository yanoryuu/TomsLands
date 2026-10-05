# レベルデザイン ログ（Jev AutoPlay で回す）

ブランチ: feature/level-design（feature/stream-intervention の上）/ 開始: 2026-10-05
道具: `Tools/TomsLands/Jev AutoPlay`（試走）・`AutoPlayLevelDesignTool`（敵・報酬・支援費を式から生成して SO に書く／勇者Lv×ダンジョンLvの勝敗表）

## 0. 前提の確認

### データの置き場所とリモート配信
| データ | 置き場所 | リモート配信で上書きされるか |
|---|---|---|
| 敵のステータス（HP/攻撃/防御） | `Assets/Battle/Datas/*.asset`（EnemyData、ダンジョン×レベルごとに専用アセット） | **されない**（balance.json の `enemies` は `slime` 1件のみで、しかも戦闘の予備リスト（BattleContext のフォールバック）にしか当たらない） |
| ダンジョンの編成・防衛報酬・魔王軍支援費 | `Assets/Resources_moved/DungeonIndoData/*.asset` の `levelDataList`（monsters / rewardGold / levelUpCost） | **されない**（balance.json の `dungeons` はスカラー値（名前・説明・推奨Lv・難易度・初期Lv）だけ。`levelDataList` は JSON に無いので SO の値が残る） |
| 勇者のレベル表（HP/攻撃/防御） | `Assets/Resources_moved/HeroStatusData.csv` | **される**（balance.json `heroLevels` が全置換。現在は CSV と同値） |
| 勇者の成長（最低保証・敗北時経験値・装備補正） | `Assets/Resources_moved/GameConstSettings.asset` | 一部（gameconst.json の `data` は initMoney・debt*・heroExpPerMob/Boss・heroBaseExpToNextLevel・blackSmithLevelUpCosts など。**最低保証・敗北時倍率・装備補正は配信に無い＝ローカル値が効く**） |
| ラン構成（フロー） | GameFlow.asset（ryoto4 のチュートリアル正本）・`flowGeneration` | 触らない |

→ 今回変えた値（敵・報酬・支援費・勝利時の最低保証）は**すべてリモート配信の対象外**なので、**Firebase へのアップロードは不要**。
`Docs/balance_upload/` には本番の gameconst.json（v2）/ items.json（v3）/ balance.json（v8）の写しを置いた（未変更）。

### AutoPlay がどの値で回っているか
`AutoPlayBatchConfig.RemoteConfig = UploadFolder`（既定）: バッチ開始時に `Docs/balance_upload/` の3ファイルを
`GameConst.OverrideFromEnvelope` / `ItemMaster.OverrideFromEnvelope` / `RemoteBalance.OverrideFromBundle` で当て、終了時に戻す
（Boot の RemoteConfig と同じ上書き）。レポートの冒頭に「リモート配信: gameconst=v2(upload) / items=v3(upload) / balance=v8(upload)」と出る。
※ これ以前の試走（〜20261005_164856）はリモート配信を当てていない（ローカル値のみ。イベント・ショップ経済の値が本番と少し違った）。

### ハーネス側の追加（このブランチ）
- 勇者の装備（`IPlayerActions.EquipHero`。HeroPanelPresenter と同じく無料で付け外し）。貪欲＝解放済み最高ランクを装備、防衛報酬狙い＝外す、Jev＝`hero_gear`（best/keep/none）を聞く
- 防衛報酬狙いボットの型: `s{N}`（Lv N まで支援）・`i`（全部重ね）・`t`（安い介入だけ）・`e`（勇者が勝ちそうなときだけ、1回で負けに変えられる最安の介入）
- streams.csv に勇者Lv・装備ランク

## 指標（Medium・20シード・初期資金 100,000G・API なし）
「通常」= 貪欲（`greedy`）。防衛報酬狙いの ROI = 防衛報酬 ÷ (支援費＋介入の純支出)。勝率は勇者の勝率。

---

## サイクル 0（変更前・リモート配信あり）
`AutoPlayReports/20261005_170649_Medium`

| ボット | 勇者勝率 | 破産 | 純資産中央値 | ROI |
|---|---|---|---|---|
| greedy | 98% | 0% | 150k | – |
| dungeon_s5（Lv5 支援のみ） | 70% | 0% | 311k | 1.48 |
| dungeon_s5i | 41% | 0% | 369k | 1.29 |

配信ごとの勝率（貪欲）: #1 90% / #2 100% / #3 100% / #4 100%。勇者Lvは配信 k 回目で Lv k（勝っても最低保証で必ず +1）。
**問題**: 敵の攻撃が勇者の防御以下（1発1ダメージ）で勇者が負けない。Lv5 支援だけで楽勝。

## サイクル 1: 敵を式で作り直す＋勝利時の最低保証レベルアップを 0 に
- 仮説: 敵の攻撃を「勇者の防御＋勇者HPの数%」で作れば、勇者Lv≒ダンジョンの強さで五分になる。勇者は勝つたびに必ず+1 なので後半ほど楽になる → 勝利時の最低保証を外す（経験値では上がる。敗北時の保証 +1 は残す＝負けると追いつく）
- 変更: `AutoPlayLevelDesignTool.Params` attackK=0.02、強さ P = 段(ダンジョン) + 0.8×(Lv−1)、`heroGuaranteedLevelUpsOnVictory` 1→0
- 結果（`171521`）: 貪欲 93%・純資産 115k。勇者が強すぎ＆防衛報酬狙い（装備を外す＋介入）が楽勝（s5 ROI 1.85・純資産 379k）
- 判断: 敵をもう少し強く（attackK 0.03）、高い段を強く、報酬の段差をならす

## サイクル 2〜4: 段の幅・報酬・支援費
| サイクル | 主な変更 | 貪欲 勝率 / 純資産 | s4 ROI | s5 ROI | メモ |
|---|---|---|---|---|---|
| 2（171613） | attackK 0.03・段 1.0/1.3/1.8/2.2/3.2/3.8・報酬 8k/20k/40k/80k/120k・支援 10k/20k/40k/80k | 84% / 148k | – | 1.13 | 後半（配信#3〜4）が 100%/90% で楽 |
| 3（171722） | 霊廟 2.5・機構城 3.8・魔王城 4.4、Lv4→5 の支援費 100k | 74% / 156k | 0.86（s4i） | 1.05 | 後半 85%/65% |
| 4（171813） | Lv4 報酬 100k・Lv5 140k | 74% / 156k | **1.46** | 1.23 | 支援だけで儲かりすぎ |

## サイクル 5〜6: 期待値で介入する防衛報酬狙い（型 `e`）と報酬の再調整
| サイクル | 変更 | s4 | s4e | s5 | s5e |
|---|---|---|---|---|---|
| 5（171937） | Lv4 90k・Lv4→5 支援 150k | 1.34 | 1.58 | 1.12 | 1.16 |
| 6（172019） | Lv4 70k・Lv5 120k | **1.10** | **1.28** | **1.01** | 1.05 |

判断: 「Lv4 は上手くやれば黒字（s4e 16/20 ラン）／Lv5 支援だけは五分（10/20）」で狙いどおり。

## サイクル 7〜9: 後半とモード別（Short / Long）
| サイクル | 変更 | Medium 貪欲 | Long 貪欲 | Short 貪欲 |
|---|---|---|---|---|
| 7（172346 / 172410 / 172443） | 機構城 4.2・魔王城 5.0 | 71% / 147k | 83% / 163k | 55% / 138k |
| 8（172524 / 172548 / 172628） | 機構城 4.8・魔王城 5.6、報酬 Lv1 15k・Lv2 30k・Lv3 45k | 57% / 192k | 68% / 173k | 45% / 148k |
| 9（172723 / 172747） | 火山牢 1.8→1.5 | **59% / 187k** | （8 と同じ） | 45% / 148k |

（以降の Jev 試走と最終確認は下に追記）
