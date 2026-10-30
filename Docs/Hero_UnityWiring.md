# 勇者の画面（案A「着せ替え型」）の配線

`Assets/Prefabs/Screens/Hero.prefab` は `Assets/Scripts/Editor/HeroUIBuilder.cs` が組む（手で YAML を直さない）。
何度流しても部品は増えない（名前で探して使い回す）。TomsShop シーンの Hero は位置・名前しか上書きしていないので、シーンは触らない。

```
# 組み直す（-nographics 可）
"C:\Program Files\Unity\Hub\Editor\6000.0.32f1\Editor\Unity.exe" -batchmode -nographics -projectPath <wt> ^
  -executeMethod HeroUIBuilder.BuildBatch -logFile <log>
# 撮る（描画が要るので -nographics は付けない。weapon / armor / drag / tactics）
"C:\Program Files\Unity\Hub\Editor\6000.0.32f1\Editor\Unity.exe" -batchmode -projectPath <wt> ^
  -executeMethod HeroCapture.RunBatch -captureOut <dir> [-captureShots weapon,drag] -logFile <log>
# 階層を書き出す
  -executeMethod HeroUIBuilder.DumpBatch -dumpOut <file>
```

## 並び（Hero > Canvas (2) > HeroPanel［HeroPanelView・UIFocusScope］）

座標は 1920x1080 の左上から。`Layout`（1920x1080・中央寄せ）の中に置く。CommonCanvas（日付 30〜646・所持金 662〜1086・メニュー 1681〜1859、どれも上 0〜203）と重ならない。

| 部品 | 位置 (x, y, 幅, 高さ) | 中身・配線 |
|---|---|---|
| `BackGround` | 全面 | 勇者背景（前のまま） |
| `Layout/HeroFigure` | 52, 188, 1000, 1000 | 立ち絵（`characterButton`。前の Chara）。押すと勇者がしゃべる |
| `Layout/HeroDropArea` | 170, 200, 560, 580 | 見えない。ここで離しても装備する（`heroDropArea`） |
| `Layout/WeaponSlot` | 36, 430, 196, 250 | `HeroEquipSlot`（武器）。`KindPlate/WeaponInfo`＝`weaponInfoButton`（ホバーで説明）、`Frame`（光らせる・つぶす単位。`Back`・`Icon`＝前の装備アイコン一式と細工の印 `CraftMark`・`GoldFrame`・持ち上げ中だけ出る `CarryGlow`）、`NamePlate/Name` |
| `Layout/ArmorSlot` | 566, 322, 196, 250 | 同上（防具。`armorInfoButton`） |
| `Layout/CraftRoot` | 30, 692, 280, 58 | 職人の仕込み（`craftRoot`・`craftFineButton`・`craftDullButton`。職人組合 Lv5 のときだけ） |
| `Layout/Back` | 26, 206, 146, 152 | もどる（`closeButton`。UIFocusScope の Cancel もこれ） |
| `Layout/StatsBoard` | 22, 776, 756, 284 | `Lv`＝`heroLevelText`、`HPLabel`/`HPNum`＝`heroHpText`、`MPLabel`/`MPNum`＝`heroMpText`、`Attack`＝`heroStatusText`（攻撃）、`Defense`＝`defenseText`、差の札 `AttackChip`/`DefenseChip`/`HpChip`（`attackDeltaText` ほか。文字の親が札）、`ClearBox`（`ClearLabel`＝`clearProbabilityInfoButton`、`ClearNum`＝`clearProbabilityText`、`Pip0〜4`＝`clearMeterPips`） |
| `Layout/ShelfPanel` | 796, 214, 1100, 584 | `Title`・`WeaponTab`/`ArmorTab`（`weaponTabButton` ほか。選んでいる方に `On` の金縁）・`Hint`・`Scroll`（`shelfScroll`。`Viewport/Content`＝`shelfContent`、2 列の GridLayoutGroup、`ScrollRectAutoFollow`）・`CardTemplate`（非表示。`cardTemplate`） |
| `Layout/DialogueBoard` | 796, 922, 1100, 138 | `Messagetext`＝`dialogueText`（前の Message を板にした） |
| `Layout/TacticsDropdown` | 796, 806, 1100, 102 | `TMP_Dropdown`＝`tacticsDropdown`。`IconRing/Icon`＝`tacticsCaptionIcon`、`Caption`＝`tacticsCaptionText`、`Desc`＝`tacticsDescText`、`InfoPlate/TacticsInfo`＝`tacticsInfoButton`（？）、`Template`（上に開く 5 行。行は `Item Icon`＋2 行の `Item Label`） |

描く順は 立ち絵 → 枠 → 板・棚 → セリフ → 作戦のプルダウン（開いた一覧が棚の上に出る）。

## カード（`HeroEquipmentCard`、494x100）

`IconBox`（アイコン背景＋`Icon`＋枠。持ち上げるとき複製してゴーストにする）・`Name`・`Info`（在庫・値段）・`Bonus`（攻撃+% / 防御+% HP+%）・`Attribute`（属性の絵＋字）・`EquippedTag`（装備中）・`Dim`（在庫 0・持ち上げ中に重ねる生成りの幕）・`WeakFrame` と `WeakTag`（弱点に合う品の赤い縁と札）。
並びは Presenter の `BuildEquipmentOptions` のまま（在庫あり → 弱点に合う → 強い順、在庫 0 は末尾）。「なし（外す）」のカードはいちばん最後。
薄くするのは CanvasGroup ではなく `Dim`（棚の並ぶ演出 `UIMotion.Stagger` が CanvasGroup の alpha を使うため）。

## 操作

- マウス: カードを勇者の枠（か体のあたり）へドラッグ。ドラッグ中は置ける枠が光り（`DragFeel.SetSlotGlow`＋`CarryGlow`）、能力値の板に付けた後の差（緑＋／赤−）が出る。置くと枠が `DragFeel.Drop` でつぶれて収まる。外で離すとゴーストが揺れて消える。縦に動かし始めたドラッグは棚のスクロールになる。
- 近道: カードを 2 回押す（ダブルクリック）。1 回目で持ち上げ（ゴーストが枠の右上に浮く）、2 回目で装備。持ち上げ中に枠を押しても置ける。
- パッド: カードで決定 → 持ち上げて置き先の枠に選択が移る → 決定で置く。B / Esc でやめる（持ち上げ中だけ UIFocusScope の Cancel を差し替える）。LB / RB で武器・防具の棚を切り替える。枠で決定すると（持っていなければ）棚をその種類にする。
- 持ち上げ中はセリフの板に置き方を出す（`OnHoverEnter`。やめる・置くと元のセリフ）。
- 省略モード（AutoPlay・人格マラソン）では持ち上げ・置く・並ぶ演出は即最終状態。

## Presenter から来るもの

`SetWeaponOptions` / `SetArmorOptions`（`HeroEquipmentOption` に名前・アイコン・属性・在庫・値段・上乗せ・弱点・付けた後の強さ `After`）、
`SetUnequipPreview`（外したときの強さ）、`SetCombatStats(攻撃, 防御, HP)`。装備の決まり（`SetWeapon`/`SetArmor`/`SetTactics`・セーブ・クリア見込み・セリフ）は前のまま。
View の Subject（`OnWeaponChanged` ほか）の形も前のまま。付けている品を置き直しただけなら何も送らない（前のプルダウンと同じ）。

## AutoPlay

`AutoPlayController.Procurement.cs` の `EquipHeroAsync` → `EquipFromShelfAsync`: `HeroPanelView.ShelfCardButton(itemId, weapon)` でカードのボタンを取り（棚がもう一方ならタブを切り替える）、`PressAsync` で 2 回押す。
作戦は前のまま `tacticsDropdown` / `tacticsValues` を名前で見る。`weaponDropdown` / `armorDropdown` / `weaponIds` / `armorIds` はもう無い。
