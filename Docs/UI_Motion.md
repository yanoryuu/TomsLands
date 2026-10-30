# UI アニメーションの共通部品（UI Motion）

画面に動きを足すときは、ここの部品を 1〜2 行で呼ぶ。部品は `Assets/Scripts/UI/Motion/`、見本の動きは
`D:\WorkTree\TomsLands\_scratch_wt04\ui_anim\gif\<番号>_<名前>.gif`（撮り方は最後の節）。

**取り付けの大原則**

- Tween は自分で `DOTween` を直接回さず、部品を呼ぶ。自前の Tween を足すときも `UIMotion.Run(tween, gameObject)` を通す（速さ・省略・SetLink がまとめて効く）。
- **最終状態は呼んだ時点で決まる**（省略モードでも同じになる）。表示・非表示・数値・SetActive を演出の完了コールバックの中だけで決めない。
- `CanvasGroup` は **alpha しか触らない**。`interactable` / `blocksRaycasts` を演出のために false にしない（AutoPlay が「押せない」と判定する）。
- SerializeField・GameObject の名前と構造は変えない（AutoPlay がリフレクションで名前を見ている）。部品は子を**足す**だけ（`ButtonFeelShine`・`NewBadge`・`IconGlint`・`PriceArrow`・`FlipPage`・`DropGlow`・`GaugeTrail`/`GaugeLead`・`CommentFrame` など。全部 `LayoutElement.ignoreLayout`）。

## 17 種の API

| # | 動き | 呼び方（例） |
|---|---|---|
| 1 | ぽよん開閉 | `UIMotion.Open(panel, window)` / `UIMotion.Close(panel, onDone, window)` / `UIMotion.IsClosing(panel)` / `UIMotion.CancelClose(panel)` |
| 2 | お金が飛んで数字が回る | `MoneyFx.Gain(fromRect, walletRect, walletText, before, after, v => $"{v:N0}G")` / 数字だけ `UIMotion.CountTo(text, from, to, fmt)` / コインだけ `CoinBurst.Fly(from, to, count, onEach, onAll)` / 浮き文字 `MoneyFx.FloatText(anchor, "+1,200G", color)` / 弾む `UIMotion.Bump(transform)` |
| 3 | 順番に並んで出る | `UIMotion.Stagger(listParent)` または `UIMotion.Stagger(rows)` |
| 4 | ボタンの手ざわり | 全ボタンに自動（`ButtonFeel`）。主ボタンだけ `ButtonFeel.SetPrimary(button)`。外す `ButtonFeel.Disable(button)`。コード生成の別 Canvas は `ButtonFeelInstaller.InstallUnder(root)` |
| 5 | ここ一番の祝い | `Celebration.Play("ランク S！", emblem: null, onDone, at: rect)` |
| 6 | 幕で場面切替 | **呼び出し不要**。`SceneLoader.Load*` が使う `LoadingView` に入っている。従来の黒フェードに戻すなら `SceneCurtain.Enabled = false` |
| 7 | タブ切替 | `TabSwitcher.MoveUnderline(underline, tab)` / `TabSwitcher.SlideContent(content, +1 or -1)` / 下線が無ければ `TabSwitcher.CreateUnderline(firstTab)` |
| 8 | アイドル | `IdleBob.Attach(iconRect)` / `IconGlint.Attach(iconRect)`（レア）/ `NewBadge.Attach(rect, "NEW")`・`NewBadge.Detach(rect)` |
| 9 | トースト | `Toast.Show("入荷しました", icon, ToastKind.Info, 2.5f)`（`Good` 緑 / `Warn` 赤）/ `Toast.ClearAll()` / 置き場所 `Toast.SetPlacement(ToastPlacement.Band(() => 帯のスクリーン矩形))`・戻す `Toast.SetPlacement(ToastPlacement.Default)` |
| 10 | ゲージの残像 | `var g = TrailGauge.Attach(fillImage); g.SetValue(0.45f);`（初期化は `SetValue(v, animate:false)`） |
| 11 | カードめくり | `CardFlip.Flip(card, () => ShowFront(), delay, rare, onDone)` / `CardFlip.FlipAll(cards, i => ShowFront(i), i => IsRare(i), onDone)` |
| 12 | 値段の変化 | 数字を差し替えてから `PriceChangeFx.Play(priceText, +1 or -1, rowBackground)` |
| 13 | つかんで置く | `DragFeel.Lift(ghost)` → `DragFeel.SetSlotGlow(slot, true)` → `DragFeel.Drop(ghost, placed)` / `DragFeel.SetSlotGlow(slot, false)` |
| 14 | ダメージ数字 | `DamageNumber.Spawn(enemyRect, 120, DamageKind.Critical)` / `SpawnAtScreen(pos, …)` / `SpawnAtWorld(worldPos, …, cam)` |
| 15 | 配信コメントの強調 | 大きいコメント・スパチャだけ表示直後に `CommentHighlight.Apply(item.Rect, CommentHighlightStyle.SuperChat)` |
| 16 | 日付めくり | `FlipCounter.Flip(dayText, DayText.DayOf(turn))` |
| 17 | 両開きの扉 | **店のホーム→施設は呼び出し不要**（`GamePanelManager.ShowTomsShopPanel` に入っている）。他で使うなら画面を切り替えた直後に `DoorTransition.Play("鍛冶屋")`、または `DoorTransition.Play(() => 切り替え, "鍛冶屋")` / すぐ消す `DoorTransition.Stop()` / 止める `DoorTransition.Enabled = false` |

### 各部品の注意

1. **Open/Close**: `window` を省くとパネル自身を拡縮する（ルート Canvas ならフェードだけ）。`Close` の閉じ演出（0.15 秒）の間、パネルはまだ `activeSelf==true`。その間に別の経路（`SetActive(true)` + `UIFx.PanelOpen`）で開き直されても、`UIFx.PanelOpen` が `CancelClose` を呼ぶので消されない。`SetActive(true)` だけで開き直す箇所は `UIMotion.Open` に置き換えるか `CancelClose` を呼ぶ。UIFocusScope（OnDisable でフォーカスを返す）は閉じ終わりまで残る。開きかけ・閉じかけに Open/Close を呼び直しても、前の演出を止めて元の大きさに戻してから基準を読むので、窓が縮んだまま残らない。
2. **MoneyFx.Gain**: 増えたときだけコインが飛ぶ（減ったときは財布が弾んで数字が回るだけ）。既存の CommonView の所持金表示（カウント・FloatDelta）は今のまま動いている。置き換えるなら二重に数えないこと。
3. **Stagger**: 位置は覚えず「横のずらし」だけを足す（`UIMotion.Nudge`）。演出中にレイアウトが位置を書き直したら、その位置を新しい元の位置として受け入れるので、一覧を作り直した直後（Destroy した古い行がフレームの終わりまで残り、新しい行が一瞬下に並ぶ）・2 回目に開いた・途中で閉じた、のどれでも最後はレイアウトどおりの位置に戻る。行に CanvasGroup を付ける（alpha のみ）。`Shake`・`TabSwitcher.SlideContent` も同じ仕組み。
4. **ButtonFeel**: 拡縮だけ触る（色・スプライト・CanvasGroup は触らない）ので ButtonHoverSprite / ColorTint / ControllerHighlight とは独立。宴のボタン（UguiButtonSe）と Animator 付き・Animation 遷移のボタンには付かない。主ボタンの脈打ちは `UIFx.Pop` などに止められても 0.5 秒後に再開する。押せないボタンの首振りは pointerClick で出る。
5. **Celebration**: 入力を受けない最前面寄りの層（並び順 31000）に出るので、下の画面のボタンは押せたまま。
6. **幕**: 幕は `LoadingOverlay` の Canvas の中（黒の Background のすぐ上）に実行時に作る（プレハブは変えていない）。閉じた幕の上の右下に、従来どおり勇者のミニ演出と「読み込み中……」を出す。起動直後の `ShowImmediate` は従来の黒のまま。
7. **TabSwitcher**: `SlideContent` は中身の今の位置からの横のずらしだけを足す（途中で呼び直しても、途中で位置が変わっても、最後は正しい位置）。LB/RB の切替と同じ処理から呼ぶ。
8. **IdleBob**: anchoredPosition を書き換えるので、**レイアウトグループが位置を決める要素には付けず、その中のアイコン（子）に付ける**。無効化・Detach で元の位置に戻る。
9. **Toast**: 画面上部の中央。新しいものが上、最大 4 枚。入力は受けない。
   - **置き場所の切り替え**: `Toast.SetPlacement(ToastPlacement.Band(func))` で「帯の中央に 1 行・幅は帯に合わせる・同時 1 件（新しいもので置き換え、古いものはその場で薄れて消える）」になる。`func` は出すたびに呼ばれ、帯のスクリーン座標（左下原点）の矩形を返す（測れなければ `null` → 既定の置き場所で出る）。スクリーン座標から演出用レイヤーの座標へは `UIMotionFx.ScreenToLayer`（`LayerPos` と同じ経路）で直すので、解像度・Game ビューの大きさが変わっても合う。板の高さは帯に合わせて 40〜64 に縮み、文字は 1 行で入らなければ 20 まで縮め、それでも入らなければ末尾を「…」にする（帯が 40 より狭いときは帯の中央から上下にはみ出す）。切り替えると出ているトーストは演出なしで消える。**画面を出るときは必ず `ToastPlacement.Default` に戻す**（既定のまま他の画面へ行くと実績トーストなども帯に出てしまう）。省略モード（AutoPlay・人格マラソン）で出さないこと、入力を受けないことは既定と同じ。
   - **配信画面（FightScene）**: `BattleSceneStarter` が配信（Phase 2）に入ったときに帯へ切り替え、決着後（リザルトの前）とシーンを出るとき（`Dispose`）に既定へ戻す。帯は `BattleSceneStarter.MeasureToastBand`: 上端 = 配信の映像の枠（`BattleWaveFxView` と同じ親でそれを囲む Image = `BattleFrame`）の下端、下端 = 売り場の棚（`StreamingSalesView.ItemSlots`）の上端、横 = 枠の幅。枠と進行ゲージ（`DungeonPhaseGaugeView`）の間が 40 以上あればゲージの上端までにするが、今の配置（1920x1080 で約 7px）では足りないので、ゲージの上に重ねて出す（2〜3 秒だけゲージが隠れる）。品出し画面（Phase 1）とリザルトは既定の置き場所。
10. **TrailGauge**: Filled の Image は fillAmount、それ以外（Slider の Fill など）は anchorMax.x を動かす。**値は必ず `SetValue` で**（直接 fillAmount を書くと残像がずれる）。減るとゲージ（Fill の親）が揺れる。
11. **CardFlip**: 表の差し替えは真横（90°）になった瞬間に `showFront` で行う。レアは 0.4 秒溜めてからめくり、後ろに金の光。
12. **PriceChangeFx**: 色・大きさ・位置・回転は必ず元に戻る（途中で次が来ても元の値を覚えている）。矢印は子の `PriceArrow`（1.2 秒で消える）。
13. **DragFeel**: ドラッグのイベントには触らない（AutoPlay が ExecuteEvents で送るドラッグもそのまま）。影は自分で足した `Shadow` だけを外す（元からの Shadow/Outline は触らない）。光る枠は枠の内側に収め（脈打ちも内側へ縮むだけ）、同じ親に同じ大きさの枠が矩形を重ねて並んでいるときは、隣との中間より内側まで縮める。持ち上げるもの（ゴースト）の大きさは呼ぶ側でそろえる（配信の売り場はアイコンの枠の大きさ）。
14. **DamageNumber**: 層の並び順は既定 1200（`DamageNumber.SortingOrder` で変更可）。配信画面の Canvas の並び順を見て調整する。
15. **CommentHighlight**: 枠はコメントの直前の兄弟（文字より後ろに描くため）として作り、毎フレーム位置を合わせる。コメントが Release（SetActive(false)）されると枠は自動で消え、使い回されても普通のコメントには枠が出ない。
16. **FlipCounter**: 本体の文字は呼んだ瞬間に新しい値になる（めくれる古い紙は一時的な子 `FlipPage`）。
17. **DoorTransition**: 0.6 秒。閉じた木の両開きの扉（中央上に施設名の金の札）→ 押されて揺れる（9°→3°）→ 奥へ開く（100°。uGUI なので `DoorLeafGraphic` が蝶番を軸にした透視の頂点を書く）→ 奥から光が差して消える。**見た目だけ**: 入力を受けない層（並び順 30500、GraphicRaycaster なし）にかぶせるだけで、画面の切り替え（SetActive）は呼ぶ側が今までどおり即座に行う（`onClosed` も Play の中で同期に呼ぶ）。だから AutoPlay・ゲームの状態から見た画面は扉なしの時と同じ瞬間に替わり、扉の下のボタンも押せる。途中でもう一度呼ぶと前の扉を消して出し直す（1 組だけ）。使う所: 店のホーム（tomsShop が表示中）から 鍛冶屋・情報屋・勇者・占い師・地図・新聞・店の設備（改装/設備）・広告・魔王軍支援 に入るとき。店へ戻る・ターン終了のまとめ・施設どうし（改装⇔設備のタブ、鍛冶屋→新聞）では出さない。扉の途中で扉の無い画面（店など）へ移ったら `Stop` で消す。札の名前は `GamePanelManager.DoorLabel`。

## 速さ・省略モード（1 箇所で切替）

`UIMotion.Mode`（`UIMotionMode.Normal / Fast / Skip`）が全部品に効く。決まり方（上が優先）:

1. `UIMotion.ForcedMode`（撮影ツール・テスト用）
2. **見せる再生**（録画中）→ ユーザー設定（通常 or 速い）。`UIMotion.SetShowcase(source, on)` か、エディタの録画中（`GameplayAutoRecorder.IsRecording`、`UIMotionEditorHooks` が `UIMotion.ShowcaseProbe` に入れる）
3. **省略要求**がある → `Skip`。`UIMotion.SetSkip(source, on)`（名前ごとに数える）
4. ユーザー設定「演出を速くする」（`GameSettings.FastEffects`）→ `Fast`（Tween の timeScale を `1 / GameSettings.EffectDurationScale` = 2 倍速）、でなければ `Normal`

- **Skip**: Tween は作った直後に `Complete(true)`（OnComplete も同期で呼ばれる）。ループ（脈打ち・アイドル・NEW の揺れ）は再生しない。トースト・祝い・ダメージ数字・浮き文字・コイン・扉は出さない（onDone・onClosed は即呼ぶ）。閉じ演出は即 `SetActive(false)`。幕は使わず即暗転／即明け。
- 自前で待つ時間は `UIMotion.Dur(秒)`（Skip なら 0、Fast なら半分）。

### どこで省略が有効になるか

| 経路 | 省略 | 場所 |
|---|---|---|
| AutoPlay（デバッグメニュー・録画連動・マラソン、どれから始めても） | `AutoPlayController.Begin` で ON、`End` と `OnDestroy` で OFF | `Assets/Scripts/Debug/AutoPlay/AutoPlayController.cs` |
| 人格マラソン（録画なし） | `StartAutoPlay` で `SetSkip("PersonaMarathon", true)`、Play を抜ける時・`Finish` で OFF | `Assets/Scripts/Editor/Recording/PersonaMarathon.cs` |
| 人格マラソン（録画あり） | `SetShowcase("PersonaMarathon", true)` で**通常**（AutoPlay の省略より優先） | 同上 |
| 録画全般（実況の撮影プリセット・手動の自動録画） | 録画中は**通常**（`GameplayAutoRecorder.IsRecording`） | `Assets/Scripts/Editor/UIMotionCapture.cs` の `UIMotionEditorHooks` |

録画ありを通常にする根拠: マラソンの録画ランは `ConfigureRecorder` で `autoPlayTimeScale = 1`・会話 `Read`、`StartAutoPlay` でも `TimeScale = run.Record ? 1f : …`・`PriceGraphPeekSeconds = 2` と「人に見せる速さ」に固定している。録画は動画の素材なので演出を出す。

## timeScale に従うか（unscaled の方針）

| unscaled（一時停止中も動く） | timeScale に従う |
|---|---|
| #6 幕、#17 扉、#9 トースト、#5 祝い、#4 ボタンの手ざわり、#13 つかんで置く、ローディングの文言のフェード | #1 開閉、#2 お金、#3 並ぶ、#7 タブ、#8 アイドル、#10 ゲージ、#11 カード、#12 値段、#14 ダメージ数字、#15 コメント、#16 日付 |

自前の Tween で選ぶときは `UIMotion.Run(tween, go, unscaled: true)`。AutoPlay の倍速（timeScale 2〜4）では timeScale に従うものは速くなる（ただし AutoPlay 中は基本 Skip）。

## AutoPlay の注意（Docs/AutoPlay_Usage.md・map.md §15）

- 演出中も `CanvasGroup.interactable / blocksRaycasts` は触らない（部品はどれも alpha だけ）。
- 閉じ演出中のパネルを AutoPlay や FindObjectsByType が拾う心配は、AutoPlay 中は Skip で即 `SetActive(false)` になるので起きない。録画中（通常モード）の 0.15 秒だけは残るので、閉じたかを判定するコードを書くなら `UIMotion.IsClosing(panel)` も見る。
- 演出用の層（トースト・祝い・コイン・ダメージ数字・扉）は GraphicRaycaster を持たないので、クリックを奪わない。
- `ButtonFeel` は AutoPlay の `PressAsync`（pointerDown → 0.12 秒 → pointerUp → onClick）でも押し込みが見える（Skip 中は何もしない）。

## GIF の撮り方

```
# 1) 17 種を連番 PNG に（描画が要るので -nographics は付けない。-only 1,5 で一部だけ）
"C:\Program Files\Unity\Hub\Editor\6000.0.32f1\Editor\Unity.exe" -batchmode -projectPath <worktree> ^
  -executeMethod UIMotionCapture.RunBatch -out D:\WorkTree\TomsLands\_scratch_wt04\ui_anim\frames -logFile <log>
# 2) GIF に（640px・ループ・3MB を超えたら幅やコマを落とす）
py -3 -I tools/ui_motion_gif.py D:\WorkTree\TomsLands\_scratch_wt04\ui_anim\frames D:\WorkTree\TomsLands\_scratch_wt04\ui_anim\gif
```

- 新しい一時シーン（保存しない）に見本 UI（店の背景・村の木と金の部品）を組み、`UIMotion.ManualUpdate = true` で全 Tween を `UpdateType.Manual` にして `DOTween.ManualUpdate` で 1 コマずつ進め、カメラを RenderTexture に描く（1280x720・30fps）。シーン・プレハブ・セーブには触れない。
- 見本を足すときは `UIMotionCapture.Demos` に 1 行と `Build〜` を 1 つ。
- 省略／通常／速いで最終状態が同じかの確認: `-executeMethod UIMotionCapture.RunSelfTest`（`-nographics` 可）。
- バッチの後は `git checkout -- ProjectSettings/Packages/com.unity.probuilder/Settings.json ProjectSettings/UtageExtensionPackageManager.asset Assets/UI/Preparation/PreparationBackground.png.meta` で戻す。

## 素材

`Assets/Resources/UIMotion/UIMotionAssets.asset` が既存の絵を指すだけ（複製しない）: コイン・きらめき・光（`Assets/UI/Village/ArrivalFx/`）、放射光・金の板・角丸の白（`Assets/UI/Village/Parts/vui_*`）。
紋章・木の幕・三角・枠線・光の帯・五芒星はコードで描く（`UIMotionFx`）。扉の板（木の板・金の帯と鋲・取っ手）は `DoorTransition.DoorTexture` がコードで描く。色は proposals.html と同じ木と金（`UIMotionFx.Wood / Gold / Cream / Red / Green …`）。
