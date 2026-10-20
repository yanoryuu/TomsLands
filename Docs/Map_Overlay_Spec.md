# 地図オーバーレイ 要件定義（ダンジョン名札・勇者の次の行き先）

最終更新: 2026-10-14 / ブランチ: feat/map-dungeon-info-overlay

## 1. 目的
地図画面（TomsShopGamePhase.Map）を開いた瞬間に、
「各ダンジョンが何で、どのくらい強く、勇者が次にどこへ行くか」を
タップせずに読み取れるようにする。

## 2. 現状
| 項目 | 現状 |
|---|---|
| MapView | 6ダンジョンのボタンのみ。文字情報なし |
| 状態表示 | 進行中(Still)のアイコンが上下に揺れるだけ。Clear/Fail は TODO |
| 詳細 | タップで DungeonInfoView ポップアップ（名前/Lv/説明/クリア確率）。情報未購入は ??? |
| 次の行き先 | 地図には出ていない（鍛冶屋ヘッダーにのみ表示） |

## 3. 機能要件

### R1 ダンジョン名札（全ダンジョン共通）
各アイコンの直下に名札（羊皮紙調のプレート）を1枚置き、以下を表示する。

| 項目 | 情報購入済み（isShowedInfo=true） | 未購入 |
|---|---|---|
| ダンジョン名 | 表示 | 表示 |
| レベル | `Lv.{currentDungeonLevel}` | `Lv.???` |
| 弱点属性 | 弱点を知っていれば（DungeonIntelModel.IsWeaknessKnown）`弱点:火` など、知らなければ `弱点:?` | 同左 |
| クリア確率 | `{p:F0}%`（ClearProbabilityCalculator、現在のダンジョンLv基準） | `??%` |

- Lvの開示ルールは既存の DungeonInfoView と揃える（未購入は ???）。
- 弱点の開示は情報購入とは別系統（新聞・情報で知ったか）。鍛冶屋ヘッダーと同じ判定を使う。
- クリア確率は勇者の装備・レベル・レリックで変わるため、地図を開くたびに再計算する。

### R2 状態表示
| DungeonStatus | 見た目 |
|---|---|
| Still（未攻略） | 通常表示 |
| Clear | アイコン・名札を減光し、「CLEAR」スタンプを重ねる |
| Fail | 「失敗」バッジを付ける（減光はしない） |

### R2b ダンジョンアイコンの見た目
- 既存の「Still なら上下に揺れる」アニメーション（MapView.SetDungeonIcon の DOLocalMoveY ループ）は**削除**する。アイコン自体は動かさない。
- 全ダンジョンのアイコンを**白い縁**で囲う（UI の Outline、または縁付きの枠画像。縁の太さはおおよそ 4〜6px）。
- 動きのある演出は R3 の勇者ドットキャラだけにする。

### R3 勇者の次の行き先
- 対象は**次の1ダンジョンだけ**。それより先の順番は見せない（新聞などの情報の価値を保つため）。
- 取得元は鍛冶屋ヘッダー・勇者画面・占い師と同じ `gameFlowManager.GetUpcomingStreamDungeon()`:
  - `gameFlowManager.IsAwaitingStream` が true → `PendingBattleDungeon`（今日配信）
  - それ以外 → `GetNextBattleDungeon()`
  - 残りは表示用の `GetDaysUntilNextStream()`（今日 0・明日 1）。`GetTurnsUntilNextBattle()` は前日に 0 になる（AutoPlay 用の意味）ので表示には使わない
- 表示:
  - 対象アイコンのそばに**勇者のドットキャラ**を立たせる（待機モーションをループ再生。アイコンの方を向く）
  - 名札の上にリボン「NEXT あとNターン」。配信待ちのときは「本日配信」
- 次の戦闘がない場合（null）は勇者キャラもリボンも出さない。

### R4 既存機能の維持
- アイコンをタップしたときの DungeonInfoView ポップアップは今のまま残す。
- 戻るボタンの挙動は変えない。

## 4. 非機能要件
- 解像度 1920x1080 で、名札の文字が背景の地図に埋もれない（プレートの背景＋縁取り）。最小フォントサイズは 22pt 程度。
- 名札がアイコン同士や戻るボタンと重ならない（アイコンの配置により名札の位置を個別に調整できること）。
- 新規の参照（名札・勇者キャラ）が未配線でも例外を出さずに動く（null-safe。プロジェクトの慣習）。
- 演出の Tween には `SetLink(gameObject)` を付ける。
- 地図画面に入るたびに最新の状態を反映する（Entry で再描画）。

## 5. 実装方針（概要）
- 名札は `MapDungeonLabel`（新規 MonoBehaviour）として1つ作り、6アイコンに同じプレハブを置く。
- `MapView` に「DungeonName → ボタン＋名札」の対応表を持たせ、今の switch 文を置き換える。
- `MapPresenter.Entry` で表示用データ（名前/Lv/弱点/確率/状態/次の行き先/残りターン）を組み立てて View に渡す。
  - 追加で注入するもの: `GameFlowManager`, `DungeonIntelModel`
- 勇者キャラは MapView 配下に1体だけ置き、次の行き先のアイコン位置へ移動させる。
- 変更の中心: `Assets/Scripts/Map/MapView.cs`, `MapPresenter.cs`, 新規 `MapDungeonLabel.cs`, `Assets/Prefabs/Screens/Map.prefab`
- 触らないもの: `TomsShopGamePhase`, `GamePanelManager`（新しい画面は追加しない）。`GameLifetimeScope` は DI の登録に変更が必要な場合だけ触る。

## 6. 決定事項（2026-10-14）
| # | 内容 | 決定 |
|---|---|---|
| D1 | 勇者のドットキャラの素材 | SPUM の勇者（`Assets/Prefabs/Hero_SPUM.prefab`、配信戦闘と同じ見た目）を使う。地図の Canvas は Screen Space - Overlay のため、画面外の撮影台（専用カメラ＋RenderTexture）で写して RawImage で表示する（`MapHeroMarker`）。モーションは SPUM の待機（IDLE） |
| D2 | 名札の背景素材 | 既存素材を流用する（新聞・鍛冶屋の枠や説明ボックスなど） |
| D3 | 白い縁の作り方 | uGUI の Outline 系で、アイコンの輪郭に沿って縁を付ける |
| D4 | 名札の情報量 | 2行（1行目: 名前と Lv、2行目: 弱点と確率）。収まらなければクリア確率はポップアップだけに出す |

## 7. 完了条件
- 6ダンジョンすべてに名札が出て、購入済み・未購入・弱点既知・未知の4通りが正しく出し分けられる。
- 次の行き先にだけ勇者キャラと「NEXT」リボンが出る。配信待ちのときは「本日配信」になる。
- Clear/Fail の見た目が出る。
- ダンジョンアイコンが揺れず、全アイコンが白い縁で囲われている。
- コンパイルエラー0、コンソールエラー0。Play してゲーム画面のキャプチャで見た目を確認済み。
