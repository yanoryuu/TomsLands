# 村（メタ層・村投資）V1 Unityエディタ配線手順

feature/village-core ブランチで実装した「村投資メタ層のコア」のエディタ手作業手順。
**シーン構築・配線・ビルド設定登録は実施済み**（このドキュメントは再現手順の記録）。
村UIが未配線でもコンパイル・起動する（VillageSceneが素通りでPreparationSceneへ直行するだけ）。

> **2026-10 更新（6施設・効果の本実装）**: 施設は領主館／冒険者ギルド／酒場（情報局）／銀行／職人組合／広場の6つ
> （詳細は Village_Meta_Design.md §0）。区画は `Plot_hall / guild / tavern / bank / artisan / plaza` の6つで、
> 旧・印刷所の区画を広場（`Plot_plaza`）に読み替え、倉庫・街道整備・農場・骨董品店・祠・工房区画・訓練所の区画はシーンから外した。
> 効果は施設レベルから `VillageEffects` が決め、店シーンの読み込み時（GameLifetimeScope）に metaData.json から読み直す。
> ラン終了時の村資金は「持ち込みの返却＋完走ボーナス＋儲け×率」、ランクは儲けで決める。以下の V1 の記述（変換率0.5・効果は表示のみ・13区画）は古い。

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

1. 6施設のマスターは `Assets/Resources_moved/Village/`（Village_hall/guild/tavern/bank/artisan/plaza）。
   建設費・効果文はマスターシート villageFacilities（1行=1施設×1レベル。GAS の readVillageFacilities_ が levels[] にまとめて balance.json に入れる）と揃えてある。
   配信（RemoteBalance.ApplyList）は同梱にある facilityId だけを上書きするので、シートから施設を消しても同梱の6つは消えない
2. 新規アセットを足したら Tools > TomsLands > **Addressables一括登録（Resources_moved）** → ラベル `VillageFacilityData` が付与される

## 2. VillageScene の構成（実施済み・構造の記録）

> **2026-08 更新（背景のタイルマップ化・建物の段階見た目）**
> - 一枚絵の Background は削除し、**Grid「VillageMap」+ Tilemap 2レイヤー**（Ground=草 / Path=土の道・広場）に変更
> - タイルはプレースホルダー（コード生成のピクセル画。`Assets/Art/VillageTiles/village_tileset.png` + `Tile_*.asset`）。
>   **本番素材への差し替えは `Tile_grass_a/b/c`・`Tile_dirt`・`Tile_dirt_light` の Sprite 参照を差し替えるだけ**で
>   塗り済みマップにそのまま反映される（タイルパレットで塗り直す場合もレイヤー構成はそのまま使える）
> - 建物は **Lv0=空き地（立て札）→ Lv1小屋 → Lv2家 → Lv3館** の段階スプライトを全13区画の
>   `stageSprites` に配線済み（`village_buildings.png`: plot_sign / building_lv1〜3、ピボット下辺中央、
>   Building の localPosition=(0,-1,0)）。差し替えも同様にスプライト参照の入れ替えのみ
> - 村全体の装飾（道の舗装化など）の発展表現は**入れない**（2026-08-30ユーザー決定。変わるのは建物のみ）
> - **町レベルの広いマップ**（48×32タイル、x -24..23 / y -18..13）。道は碁盤目ではなく
>   ゆるく蛇行しながら数本分岐するネットワーク（西⇔東の街道 / 北=領主館へ+左右分岐 / 南 / 脇道3本）。
>   施設13区画は道沿いに分散配置（中央広場=スポーン+出撃、北端=領主館）
> - **カメラはプレイヤー追従**: Main Camera に `CameraFollow2D`（target=Player、
>   worldBounds=マップ範囲でクランプ、SmoothDamp追従）。UIはScreen Space - Cameraなのでそのまま追従

```
VillageScene.unity（ビルド設定登録済み）
├─ Main Camera（orthographic size=5.5, pos(0,-0.5,-10)）
├─ VillageMap（Grid）
│    ├─ Ground（Tilemap: 草タイル。sortingOrder=-20）
│    └─ Path（Tilemap: 土の道・出撃広場。sortingOrder=-19）
├─ Player（TomsShopのPlayerを複製: PlayerMove+SPUM+Rigidbody2D）
├─ Plots/Plot_{facilityId} ×13（FacilityPlot + BoxCollider2D(Trigger)）
│    ├─ Building（SpriteRenderer。stageSprites=[立て札, Lv1小屋, Lv2家, Lv3館] 配線済み）
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
- FacilityPlot の `facilityId` はマスターの `facilityId`（hall/guild/tavern/bank/artisan/plaza）と一致させること
- 建物プレースホルダーは自前生成のピクセル画（`Assets/Art/VillageTiles/village_buildings.png`）。
  製品版アートでは同名スプライトの差し替え、または各区画の `stageSprites` を個別に差し替える

## 3. 動作確認チェックリスト（V1で確認済みの項目）

1. VillageScene直行（帰還レポートあり）→ 収支ポップ「今回の稼ぎ→村へ+N G」カウントアップ表示 ✓
2. 施設を調べる → 投資パネル（未建設/現在効果/次Lv効果/費用）✓
3. 建設する → 村資金減・総合Lv+1・建物スプライト変化・メッセージ表示 ✓
4. 領主館ゲート: ギルドLv1→Lv2 は「領主館Lv1でLv2に拡張可能」でボタン不可 ✓ / 祠は領主館Lv2まで未解禁表示 ✓
5. 出撃準備へ → PreparationScene に遷移 ✓
6. metaData.json に villageFunds/facilities が保存・復元される（スロット跨ぎ永続）✓

## 3-1. 6施設化（2026-10）で足した確認項目

1. 投資パネルの効果文が6施設の新しい文になっている（バッチで 1920x1080 / 1280x800 のキャプチャを確認）
2. 既存セーブ: 削除した施設に投資済みなら、読み込み時に統合先のレベルへ読み替え／返金され、Console に `[Meta] 村の施設の統合:` が出る
3. 町人は「総合Lv ÷ 最大の総合Lv（17）」の割合で増える（VillagePopulationView.ApplyScaled）
4. 初めて村に入ったとき: 前半の会話 → 帰還演出 → 後半の会話（VillageTutorialDirector の betweenParts）

## 3-2. 村の格（2026-10-21）で足した配線（Village_Meta_Design.md §0-7）

- `VillageUI/RankNextText`（VillageLevelText の複製・26pt）→ `VillageView.rankNextText`。次の格の条件（王都では称号）。
- `VillageUI/RecordBoardPopup`（ConversionPopup の複製。Title / BodyText / CloseButton「閉じる」）→ `VillageView.recordBoard*`。
- `Plots/Plot_records`（FacilityPlot・facilityId = `records`、Stage0 に Cainos の Billboard 01、Bubble は Plot_plaza の複製）→ `VillageView.plots` の末尾。王都になるまで非表示。
- 格が上がった瞬間のポップは ConversionPopup を流用（EarnedText と ConvertedText の位置を入れ替えて、格の名前を大きい金の行に出す。帰還報告では戻す）。
- `ResultScene` の `StatsPanel/MerchantTitle`（周回の行の複製・金色 28pt、枠の下端の上）→ `ResultView.merchantTitleText`。
- 確認（2026-10-21、エディタの Play・slot_0 に検証用の metaData を書いて確認し、元に戻した）: 準備画面の鍵、デバッグの施設 +1 と投資での格上げ（ポップ→会話）、王都の掲示板、タイトルのセーブ欄の称号。リザルトの称号は編集モードでの表示のみ（Result を直接読み込まない決まりのため）。

## 4. 未確認・今後（V2以降）

- タイトル「ニューゲーム」→村、Result/GameOver→村 の通しプレイ（コード実装済み・実ラン未確認）
- 施設効果の実適用: V2=開始時型（初期資金・持ち込み枠・初期Lv・フォロワー・借入枠段階）/
  V3=レリック解禁（unlockGuildLevel+抽選フィルタ）/ V4=常時型（Resolver注入・農場・工房区画）/ V5=産業投資
- 村人（VillagerCharacter）はV1未実装（企画書§13-F）
- WASD移動＋[E]調べるの実機での手触り確認（自動テストでは Subject 直叩きで代替）
