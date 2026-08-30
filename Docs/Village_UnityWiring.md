# 村（メタ層・村投資）V1 Unityエディタ配線手順

feature/village-core ブランチで実装した「村投資メタ層のコア」のエディタ手作業手順。
**シーン構築・配線・ビルド設定登録は実施済み**（このドキュメントは再現手順の記録）。
村UIが未配線でもコンパイル・起動する（VillageSceneが素通りでPreparationSceneへ直行するだけ）。

## 仕様の要点（詳細: Docs/Village_Meta_Design.md）

- **フロー**: タイトル（ニューゲーム）→ **VillageScene** → PreparationScene → ラン。
  ラン終了（Result/GameOver）→ **村へ帰還**（精算→ラン内セーブ削除→遷移の順を厳守）。続きからは従来どおりTomsShop直行
- **村と店の経営（ラン）は完全に別フロー**。橋はラン終了時の変換のみ:
  クリア=純資産×`village.conversionRate`(0.5) / 破産=現金×`bankruptcyConversionRate`(0.1)。ラン中送金は不採用
- **歩ける村**: PlayerMove（店と同じ操作）で移動し、施設区画（FacilityPlot）に近づく→吹き出し→
  [E]/Enter/Space かクリックで投資パネル。常設の[出撃準備へ]ボタンでいつでも出撃（テンポ保険）
- **純資産の修正**: ResultModel の純資産に金融資産（債券・ファンド評価額）を算入するようになった
- **セーブ**: `metaData.json` に `villageFunds` / `facilities`（施設Lv）を追加（RunSaveCleaner対象外のまま）
- **配信**: `gameconst.json` の `village` 区画（変換率）+ `balance.json` の `villageFacilities` リスト区画（facilityIdキー）
- V1の施設効果は**表示のみ**（effectText）。実効果の適用は V2（開始時型）/V3（レリック解禁）/V4（常時型）で実装

## 1. マスターデータ（実施済み）

1. Tools > TomsLands > データ生成 > **村施設データ生成（全部入り）** → `Assets/Resources_moved/Village/` に13施設
2. Tools > TomsLands > データ生成 > **Addressables一括登録（Resources_moved）** → ラベル `VillageFacilityData` が13件付与

## 2. VillageScene の構成（実施済み・構造の記録）

```
VillageScene.unity（ビルド設定登録済み）
├─ Main Camera（orthographic size=5.5, pos(0,-0.5,-10)）
├─ Background（背景_街_1 を少し暗めに）
├─ Player（TomsShopのPlayerを複製: PlayerMove+SPUM+Rigidbody2D）
├─ Plots/Plot_{facilityId} ×13（FacilityPlot + BoxCollider2D(Trigger)）
│    ├─ Building（SpriteRenderer。プレースホルダー=マップ町アイコン: -1=未建設/-0=建設済み）
│    ├─ SignIcon（施設アイコン看板。icon未設定なら非表示）
│    ├─ Bubble/Name+Hint（接近時の吹き出し・ワールドTMP）
│    └─ LockedSign（未解禁表示）
├─ Walls（境界コライダー4枚）
├─ VillageUI（Canvas=ScreenSpaceCamera+VillageView）
│    ├─ HUD: VillageFundsText / MetaCurrencyText / VillageLevelText / TitleButton / MessageText / DepartButton
│    ├─ InvestPanel（6枠: アイコン/名前/Lv/現在効果/次Lv効果/費用/建設ボタン/閉じる）
│    └─ ConversionPopup（帰還収支: タイトル/稼ぎ/村へ+N Gカウントアップ/村を見る）
├─ EventSystem（InputSystemUIInputModule）
└─ VillageLifetimeScope（villageView 配線済み）
```

- **注意: CanvasはScreen Space - Camera**（Overlayだと一部キャプチャ/合成で映らない。プロジェクト慣習にも一致）
- FacilityPlot の `facilityId` はマスターの `facilityId`（hall/guild/antique/shrine/bank/warehouse/road/press/artisan/tavern/workshop/farm/training）と一致させること
- 建物のプレースホルダーはマップの町アイコン5種を巡回使用。製品版アートで各区画のBuilding/stageSpritesを差し替える

## 3. 動作確認チェックリスト（V1で確認済みの項目）

1. VillageScene直行（帰還レポートあり）→ 収支ポップ「今回の稼ぎ→村へ+N G」カウントアップ表示 ✓
2. 施設を調べる → 投資パネル（未建設/現在効果/次Lv効果/費用）✓
3. 建設する → 村資金減・総合Lv+1・建物スプライト変化・メッセージ表示 ✓
4. 領主館ゲート: ギルドLv1→Lv2 は「領主館Lv1でLv2に拡張可能」でボタン不可 ✓ / 祠は領主館Lv2まで未解禁表示 ✓
5. 出撃準備へ → PreparationScene に遷移 ✓
6. metaData.json に villageFunds/facilities が保存・復元される（スロット跨ぎ永続）✓

## 4. 未確認・今後（V2以降）

- タイトル「ニューゲーム」→村、Result/GameOver→村 の通しプレイ（コード実装済み・実ラン未確認）
- 施設効果の実適用: V2=開始時型（初期資金・持ち込み枠・初期Lv・フォロワー・借入枠段階）/
  V3=レリック解禁（unlockGuildLevel+抽選フィルタ）/ V4=常時型（Resolver注入・農場・工房区画）/ V5=産業投資
- 村人（VillagerCharacter）はV1未実装（企画書§13-F）
- WASD移動＋[E]調べるの実機での手触り確認（自動テストでは Subject 直叩きで代替）
