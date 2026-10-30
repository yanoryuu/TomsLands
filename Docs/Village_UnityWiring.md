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
> - ~~村全体の装飾（道の舗装化など）の発展表現は入れない（2026-08-30ユーザー決定）~~ → 2026-10-30 に撤回。村の格で地面と装飾が育つ（§3-3）
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
│    ├─ HUD: FundsBox(VillageFundsText) / RankBanner(VillageLevelText・RankText・星・旗・冠) / RankNextPlate(RankNextText)
│    │        / TitleButton（右上） / MessageBand(MessageText・下中央) / DepartButton（右下）  ※2026-10-30 案A（§3-4）
│    ├─ InvestPanel（木の枠: アイコン/名前/Lv/現在・次Lvのカード/費用・村資金/投資ボタン/×）
│    ├─ ConversionPopup（帰還収支・格上げ兼用: タイトル/儲け/村へ+N G/BreakdownArea(内訳)/村を見る）
│    └─ RecordBoardPopup（自己ベスト: タイトル/BodyBox(BodyText)/閉じる）
├─ VillageRankDecor（格ごとの地面・装飾のプレハブ。ルート・(0,0,0)）
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

## 3-3. 村の格ごとの見た目（2026-10-30）

マップの広さと道の形はそのまま、格が上がるほど地面と装飾が育つ（積み上げ式。開拓地だけのものは村・町で片づく）。

| 格 | 地面 | 装飾 |
|---|---|---|
| 0 開拓地 | 土の道 | 広場の焚き火とテント・野営地・資材の山・荷車・切り株・柵で囲った空き地と「売地」の立て札 |
| 1 村 | 土の道 | 空き地に民家4軒・畑（麦／野菜とかかし）・花・木の看板・広場の掲示板と鉢植え・街灯少し（切り株は残る） |
| 2 町 | 広場・北の道・南の道の入口が石畳 | 民家・倉庫が増える・露店4つ・樽や木箱・花壇・街灯・赤い旗 |
| 3 王都 | 道がぜんぶ石畳 | 南の城壁と門・門の像・広場の噴水（焚き火のあった所）・礼拝堂・物見の塔・旗と街灯がさらに増える |

- 施設が最大Lv（Lv5）の区画は建物の両わきに**金の旗**が立つ（Lv3 と見分ける）。
- 中身: `Assets/Prefabs/Village/VillageRankDecor.prefab`（`VillageRankDecor`。`layers` = 格の範囲ごとのまとまり、`maxLevelDecor` = 施設ごとの金の旗）。
  作り直しは `VillageRankDecorBuilder.BuildBatch`（バッチ・-nographics。座標は同スクリプトの Layout*）。歩けるかの検査（スポーンから全区画と南の端まで・道のマス）も同時に走る。
- 石畳・旗・噴水の絵は Cainos に無いのでコードで描いた（`Assets/Art/VillageTiles/village_rank_paving.png` / `village_rank_props.png`）。石畳は Path（土の道）と同じマスに Ground/-85・-84 で重ねる。
- 草地の継ぎ目の細い線: 草のタイルは `TX Tileset Grass.png` の 128×128 の塊から切ってあり、塊の外が透明なため、拡大率が整数倍でないときに隣の透明を拾って線になっていた。
  塊の外の空き枠を隣の色で 2px 塗り広げて直した（`VillageRankDecorBuilder.FixGrassSeams`。使っている絵は変わらないので Cainos のデモシーンにも影響なし）。
- `VillagePresenter.RefreshAll` が `VillageRankDecor.Apply(格)` と `ApplyFacilityLevel` を呼ぶ（シーンに無ければ何もしない）。投資で格が上がったときは新しく出た装飾が弾んで出て、石畳がふわっと敷かれる。
- キャプチャ: `tools/village_capture.ps1 -Shots overview,hud -Ranks 0,1,2,3`（プレハブを一時的に置いて撮る）。

**シーンへの配置（2026-10-30 済み）**: VillageScene のルートに `VillageRankDecor.prefab` を位置 (0,0,0) で置き、`OcclusionSilhouette`（OccluderStencilSetup）の `roots` の末尾に足した（建物の陰でプレイヤーのシルエットが出るように）。`VillageUIBuilder.BuildBatch` が毎回確かめ、置いてあれば置き直さない・二重に足さない。

## 3-4. 村画面の UI（案A「木と金縁」・2026-10-30）

ユーザーが 3 案から案A を選んだ。シーンは `VillageUIBuilder.BuildBatch`（`-uiVariant` 省略で A。B/C は比較用に残してある）で組んで保存する。
何度実行しても部品は名前で探して使い直すので重複しない（2 回続けて流して、変わるのは位置と大きさだけなのを確認済み）。

```
Unity.exe -batchmode -nographics -projectPath <wt> -executeMethod VillageUIBuilder.BuildBatch -logFile <log>
```

- 部品: `Assets/UI/Village/Parts/vui_*.png`（既存 PNG の複製・加工。9 スライスの縁は `VillageUIBuilder.PartBorders`）。今回足したもの:
  - `vui_panel_frame.png` … `新聞/1枠.png` の右上の飾りを左上の鏡写しで消したもの（投資パネル。× を置く場所）
  - `vui_gold_btn_off.png` … `新聞/4−0購読する.png` をグレーにしたもの（押せないボタン。透かさずグレーアウト）
- 配線の変更（`VillageView`。どれも未配線なら従来どおり）:
  - `titleButton` ← `VillageUI/TitleButton`（右上の控えめな茶色の板「タイトルへ」。ColorTint・無効はグレー）。押すと `OnGoTitle` → TitleScene（d36458c3 で外れていたのを戻した）
  - `messageText` ← `VillageUI/MessageBand/MessageText`、新規 `messageBand` ← `VillageUI/MessageBand`（画面下中央の木の帯。出店準備ボタンの左。文字が空なら帯ごと隠す）
  - 新規 `conversionBreakdownArea` ← `ConversionPopup/BreakdownArea`（内訳をポップの中に入れる枠。内訳があるときだけ出し、その高さだけポップを縦に伸ばす。格上げのポップでは隠れて元の高さ）。
    ConversionPopup の見出し・儲け・村へ は上寄せ、ボタンと BreakdownArea は下寄せのアンカーにしてある
  - 新規 `investLabelDisabledColor`（投資ボタンが押せないときの文字の色。押せるときの色は最初に開いたときに覚える）
  - `recordBoardBodyText` は `RecordBoardPopup/BodyBox` の子へ移した（参照はそのまま）
- 見た目の統一: 投資パネル・帰還ポップ・記録掲示板は同じ木の枠（`1枠` 系・ppu 1.9）、見出しは白＋アウトライン、ボタンは同じ金の板（無効はグレーの板に差し替え）。
- 投資パネルの × は飾りを消した右上の角の内側（(-74,-74)・56px）に置き、枠の飾りと重ならない。
- キャプチャ: `tools/village_capture.ps1 -Shots hud,invest,conversion,rankup,record,overview -Levels 0,3,5 -Ranks 0,3`。
  `-Funds 9999999` で HUD の村資金を上書きでき、「足りる＝金色・ボタン有効」の投資パネルが撮れる。`rankup`（格上げのポップ）と `record`（自己ベストの掲示板）を足した。
- 未確認（Play でしか見られない）: 宴の会話ウィンドウとメッセージの帯の重なり、ボタンのハイライト・押下の色、格上げのポップの拡大アニメ、パッド操作で「タイトルへ」にフォーカスが移ること。

## 3-5. 施設へのショートカット・小窓の幕・デザインレビューの直し（2026-10-30）

同じく `VillageUIBuilder.BuildBatch` で組む（手でシーンをいじらない）。描く順は「HUD → 施設の帯 → 幕 → 格上げの光 → 小窓」（Build の最後で並べ直す）。

- 施設へのショートカット（`VillageUI/FacilityTabs`）: 左端・縦中央に「施設」のつまみ（濃い茶＋金縁）を常に出し、押すと施設ボタンの帯が左から滑り出る（DOTween 0.25 秒・OutCubic、閉じは InCubic。`SetLink`。連打しても今の位置から向きを変えるだけ）。矢印は閉じ「→」／開き「←」。
  - 配線（新規・どれも未配線なら帯ごと出ない）: `shortcutDrawer` ← `FacilityTabs`（閉じた位置 x=-340 で保存）、`shortcutKnob` ← `FacilityTabs/Knob`、`shortcutKnobArrow` ← `Knob/Arrow`、`shortcutButtonTemplate` ← `FacilityTabs/List/ButtonTemplate`（非アクティブ。子に Icon / Name / Level / Badge）、`shortcutListGroup` ← `FacilityTabs/List`（閉じている間は押せない）、`recordBoardIcon`（星）。
  - ボタンは Presenter が `RefreshAll` のたびに `view.SetShortcuts` で並べ直す（マスターの施設を 領主館 → 建設に要る領主館Lv の順、同じならマスターの順。名前はマスター）。Lv は「Lv3」・最大は「MAX」・未建設は「未建設」。`VillageModel.CanInvest` が通る施設に金の「！」。
    記録掲示板は区画（`records`）が立つ格（王都）のときだけ足す。押すと `OnShortcutFacility` → `SelectFacility`（区画を調べたのと同じ）。
  - 受け付けない: `IsBusy`（帰還演出・会話・格上げ）・`SetUiInputLocked` 中・小窓を開いている間。演出が始まったら帯は自動で閉じ、つまみはグレー。帯は投資パネルと重ならない（パネルは中央 840 幅）ので開いたまま、幕の下になる。
  - キー/パッド: Tab キー・パッドの LB で開閉（キーの Q/E は既存の TabLeft/Right と区画の「調べる」に使っているので使わない）。パッド/キーで開くと先頭のボタンを選ぶ。
- 小窓の後ろの幕（`VillageUI/ModalDim`・`modalDim`）: 投資パネル・帰還/格上げポップ・記録掲示板のどれかが開いている間だけ全画面の黒を敷く。不透明度は `modalDimAlpha = 0.8`（リニア色空間なので見た目でおよそ 55%）。
  開閉は View の各 Show/Hide から `RefreshModalDim` を呼び、`LateUpdate` でも合わせる（AutoPlay 等で直接閉じても残らない）。幕を押すと投資パネル・掲示板は閉じる（帰還/格上げはボタンで閉じる）。
- 投資パネル: 最大レベルはボタンの代わりに `maxLevelPlate`（金縁＋濃い茶 #4A2A14 の「MAX」の札）を出し、費用の行（`costCaptionText`・費用・村資金）を隠す。未建設は `currentEffectRow` を隠して `nextEffectRow` をその位置へ上げる。
  「特殊な効果」は見出しを「現在」「次Lv」と同じ大きさ・色にし、強調は `specialFrame` を左の 8px の金の帯にした。効果文は `VillageView.KeepPhrases` で「・」「、」「：」と助詞の後でだけ折り返す（`<nobr>` で包む）。
  費用が足りるときの色は緑 #7CD36A（アウトライン付き）。パネルは 840 幅・カード 740 幅。
- 格上げのポップ: `ShowRankUpPopup(..., fromRank, toRank)`。枠を `vui_panel_frame_gold`（`rankUpPopupSprite`）にし、上に格ごとの紋章（`rankUpEmblems`: 村=緑の旗・町=紋章の盾・王都=王冠）、星の「前→後」（`rankUpStarsBefore/After`。増えた星は大きく弾む）、後ろで回る放射光（`rankUpRays` = `vui_rays`。コードで描いた PNG）。ポップは星の行のぶん 76px 縦に伸びる。
- 記録掲示板: 3 列の表（`recordBoardBodyText`=難易度・左揃え、新規 `recordBoardAmountText`=金額・右揃え、`recordBoardRankText`=ランク・中央）。Presenter は `ShowRecordBoard(title, IReadOnlyList<RecordRow>)` を使う。
- HUD: 村資金の箱は `vui_funds_box`（`基本/3ゴールド.png` の 9 スライス版）372x104、文字は「村資金」を小さく上・額を 40px 固定（7 桁でも縮まない）。
  看板は 124px に詰め、星と「町・総合Lv18」を `RankBanner/RankRow` に横並び、次の格の札は看板の下の縁に吊るす（上の帯は約 168px）。王都の看板は `vui_wood_plate_gold`（角の飾りを他と揃える）、王冠は枠の内側へ。
  小窓の枠は帰還ポップ・記録掲示板も `vui_panel_frame`（右上の渦で内側の線が途切れないように）。
- 撮影: `-Levels` だけでもレベルから格を決めてその格の地面・装飾を置く。投資パネルの押せる/押せない・MAX の札は `VillageModel.CanInvest` と同じ判定。開いた帯は `tabs`、閉じた帯は `hud` で見る。
- 未確認（Play でしか見られない）: 帯の滑り・連打・矢印の向き、演出中につまみがグレーになって帯が閉じること、Tab/LB での開閉とパッドの選択、幕のフェードと押して閉じる動き、格上げの拡大・光の回転・星の弾み。

## 4. 未確認・今後（V2以降）

- タイトル「ニューゲーム」→村、Result/GameOver→村 の通しプレイ（コード実装済み・実ラン未確認）
- 施設効果の実適用: V2=開始時型（初期資金・持ち込み枠・初期Lv・フォロワー・借入枠段階）/
  V3=レリック解禁（unlockGuildLevel+抽選フィルタ）/ V4=常時型（Resolver注入・農場・工房区画）/ V5=産業投資
- 村人（VillagerCharacter）はV1未実装（企画書§13-F）
- WASD移動＋[E]調べるの実機での手触り確認（自動テストでは Subject 直叩きで代替）
