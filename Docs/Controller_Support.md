# ゲームパッド対応（共通基盤）

方式: **フォーカス移動式**。十字キー/左スティック（キーボードは矢印・WASD）でボタン間を移動、A/Enter で決定、B/Esc で戻る/閉じる。

> ボタンガイド（画面下の「Ⓐ決定 Ⓑ戻る」表示）は他 UI と重なるため**採用しない**（2026-10 方針変更で削除済み）。

コードは `Assets/Scripts/ControllerInput/`、選択枠は `Assets/Prefabs/ControllerOverlay.prefab`。

---

## 1. 仕組み

| 部品 | 種別 | 役割 |
|---|---|---|
| `ControllerInput` | static | 最後に使った入力デバイス検知（R3）。パッド用ボタンの押下通知 |
| `UIFocusManager` | static | `UIFocusScope` のスタック管理、選択の維持、Cancel/補助ボタンの配送 |
| `ControllerSupportRunner` | 自動生成 | 起動時に `[ControllerSupport]`（DontDestroyOnLoad）を生成し上 2 つを毎フレーム回す。シーン配置不要 |
| `UIFocusScope` | MonoBehaviour | 画面/パネル/ポップアップのルートに付ける「フォーカスの単位」 |
| `ControllerHighlight` | MonoBehaviour | 選択中要素への金縁＋パルスの選択枠（ControllerOverlay 内）|
| `ControllerOverlayBootstrap` | MonoBehaviour | 全シーンの EventSystem に付与済み。ControllerOverlay を 1 個だけ生成し常駐させる |
| `UIFocusHighlightOverride` | MonoBehaviour | 要素ごとの枠の調整（非表示・合わせる矩形・余白）|
| `ScrollRectAutoFollow` | MonoBehaviour | ScrollRect に付けるだけで選択要素へ自動スクロール |
| `PointerEventBridge` | MonoBehaviour | Pointer 系しか持たない部品をパッドで動かす橋渡し |

### デバイス判定
- パッド: いずれかのボタン押下 / スティック 0.5 超 → `Gamepad`
- キーボード: 矢印・Enter・Esc・Tab・F・R の押下 → `Keyboard`（入力欄フォーカス中は除外）
- マウス: 移動(4px超)・クリック・ホイール、タッチ → `Mouse`
- `IsNavigating`（Keyboard or Gamepad）のとき: 選択枠を出す・何も選ばれていなければ自動選択する
- `Mouse` に切り替わった瞬間に選択を外す（ハイライト残り防止。入力欄の入力中は外さない）
- パッド表記: `ControllerInput.Style`（Xbox / PlayStation / Nintendo）。DualShock/DualSense を自動判別

### 選択の維持（UIFocusManager.Tick, LateUpdate）
パッド/キー使用中に次のどれかになったら選び直す。
- 何も選択されていない／選択要素が非アクティブ・操作不可になった
- 最前面スコープが変わった（開いた・閉じた）→ 戻り先は「そのスコープで最後に選んでいた要素」
- 最前面スコープが `modal` で、選択がその外に出た → 直前の要素に戻す

選び直し先: 最前面スコープの `lastSelected` → `firstSelected` → 子の最初の操作可能な Selectable（Scrollbar は後回し）。
**スコープが 1 つも無い画面**では、直近に `NotifyPanelShown` されたパネル内（無ければ全体）から「最前面 Canvas の左上」の Selectable を選ぶ（フォールバック。`PlayerInput` が有効な村シーンでは行わない。`UIFocusManager.FallbackSelectionEnabled=false` で停止可）。

### 入力アクション
`Assets/InputSystem_Actions.inputactions`（プロジェクト共通アクション）の **UI マップ**に統一。全 9 シーンの `InputSystemUIInputModule` がこれを参照。

| アクション | パッド | キーボード | 用途 |
|---|---|---|---|
| Navigate | 左/右スティック・十字 | 矢印・WASD | フォーカス移動（EventSystem）|
| Submit | A（South）| Enter/Space | 決定（EventSystem）|
| Cancel | B（East）| Esc | 最前面スコープの Cancel |
| TabLeft / TabRight | LB / RB | Q / E | スコープの onTabLeft/Right |
| ButtonX / ButtonY | X（West）/ Y（North）| F / R | スコープの onButtonX/Y |
| Menu | Start | —（Esc は各画面の Cancel で扱う）| スコープの onMenu |
| Point/Click/RightClick/MiddleClick/ScrollWheel | — | マウス | 従来どおり |

---

## 2. 画面を対応させる手順（後続担当向けチェックリスト）

1. [ ] 画面（パネル）のルートに **`UIFocusScope`** を付ける
   - `firstSelected`: 最初に選ばせたいボタン（未指定なら子の最初の操作可能な Selectable）
   - 決定を誤爆すると困るボタン（営業開始・購入確定など）は firstSelected にしない
2. [ ] **戻る**がある画面: `cancelMode = ClickButton` ＋ `cancelButton` に戻るボタン。
   ボタンが無い処理なら `InvokeEvent` ＋ `onCancel`、コードなら `scope.SetCancelAction(() => ...)`
3. [ ] 画面内ポップアップ/サブパネル（SetActive で出すもの）にも `UIFocusScope` を付け、`modal=ON`・`priority=10` 程度
4. [ ] タブがある画面: `onTabLeft/onTabRight` にタブ切替を登録（LB/RB, Q/E）
5. [ ] 一覧（ScrollRect）に **`ScrollRectAutoFollow`** を付ける
6. [ ] Pointer イベントしか無い部品（ホバー説明・ドラッグ前提スロット等）に **`PointerEventBridge`** を付ける
7. [ ] 動的生成のリスト: 再構築後に `scope.Refocus(resetToFirst: true)`
8. [ ] Button の Navigation は基本 Automatic のままで良い。変な飛び方をする所だけ Explicit で上書き
9. [ ] 枠が大きすぎる/不要な要素には `UIFocusHighlightOverride`（`hide` / `target` / `padding`）
10. [ ] 確認: パッド（またはキー矢印/Enter/Esc）で全ボタンに到達・決定・戻るができる。マウスに戻すと枠が消える

### コード例
```csharp
// Presenter/View から（R3）
scope.OnButton.Where(b => b == ControllerButton.Y).Subscribe(_ => OpenDetail()).AddTo(this);
ControllerInput.IsGamepad.Subscribe(on => hintIcon.SetActive(on)).AddTo(this);

// 任意の要素へフォーカス（マウス操作中は記憶だけ）
scope.Select(buyButton);

// 会話（Utage）など別系統の入力が前面の間、Cancel/LB/RB/X/Y/Start の配送を止める
var block = UIFocusManager.BlockScopeInput();  // 終わったら block.Dispose();
```

---

## 3. 各コンポーネントのリファレンス

### UIFocusScope
| 項目 | 説明 |
|---|---|
| firstSelected | 最初に選ぶ要素 |
| autoSelect | OFF で自動選択しない（フィールド移動画面などに置いて「何も選ばない」を宣言）|
| rememberLastSelected | 戻ってきたとき最後の選択を復元 |
| modal | ON の間、スコープ外へフォーカスが出ない |
| priority | 大きいほど前面。同値なら後から有効化された方が前面 |
| dpadOnlyNavigation | ON のスコープが最前面の間、UI/Navigate のスティック・WASD バインドを無効化（空パスのオーバーライド）し、選択移動を十字キー・矢印キーだけにする。最前面でなくなると元に戻す（`UIFocusManager.Tick`）|
| cancelMode / cancelButton / onCancel | None / ClickButton（押下アニメ付きで Submit を送る）/ InvokeEvent |
| SetCancelFallback(action)（コード）| Cancel を誰も処理しなかったとき（cancelButton が非表示・押せない、None）に呼ぶ |
| onTabLeft/onTabRight/onButtonX/onButtonY/onMenu | 最前面のときだけ呼ばれる |

API: `GetOrAdd(go)`, `SetCancelButton(btn)`, `SetCancelAction(action)`, `Select(selectable)`, `Refocus(resetToFirst)`, `OnButton`(Observable), `IsTop`, `Modal/Priority/AutoSelect/FirstSelected` プロパティ。

### UIFocusManager（static）
`Top`, `NotifyPanelShown(panel)`, `RequestRefocus()`, `Select(go)`, `BlockScopeInput()`, `IsUsable(go)`, `AttachSceneScope(sceneName, onCancel, priority=100)`（Additive ポップアップシーンにコードでモーダルスコープを付ける）, `FallbackSelectionEnabled`。

### ControllerInput（static）
`Device` / `IsNavigating` / `IsGamepad` / `Style`（ReadOnlyReactiveProperty）, `OnButtonPressed`（Observable<ControllerButton>。入力欄フォーカス中は発火しない）, `AnyPressedThisFrame()`（旧 `Input.anyKeyDown` 代替）, `FindAction("UI/TabLeft")`, `DebugSetDevice(kind, style)`（キャプチャ用）。

### ScrollRectAutoFollow
ScrollRect に付けるだけ。`margin`（余白 px）, `duration`。`ScrollTo(rect, instant)` をコードからも呼べる。マウス中は動かない。

### PointerEventBridge
- 選択 → PointerEnter / 選択解除 → PointerExit / 決定 → PointerDown→Up→Click を `targets`（空なら自分）へ転送
- パッド/キー使用中のみ転送（マウス時は本物のイベントのみ）
- Selectable が無ければ自動付与（Transition=None）。同じ GameObject に Button 等がある場合、クリック系は二重発火防止のため自分には送らない

### 選択枠（ControllerOverlay.prefab）
Overlay Canvas（sortingOrder 32000、GraphicRaycaster なし＝クリックを奪わない）。`FocusFrame`（Glow＋Frame の 9 スライス、素材 `Assets/UI/Controller/ctrl_focus_frame.png`, `ctrl_focus_glow.png`）が選択要素の画面矩形へ追従（移動は補間・出現時に軽いポップ・パルス）。選択要素の CanvasGroup の alpha に合わせて枠も薄くなる（UIFx.PanelOpen のフェードと同期）。
再生成: `Tools/Controller/Build Overlay Prefab`（`ControllerSupportSetup.cs`）。

---

## 4. 既存コードへのフック（最小変更）
- `GamePanelManager.ShowWithFx`: `UIFocusManager.NotifyPanelShown(panel)` を 1 行追加。パネル切替直後に旧パネルの選択を捨て、新パネル（スコープがあればそれ、無ければパネル内フォールバック）へフォーカス。alpha 0 の間は 0.1 秒ごとに再試行するので PanelOpen のフェード中でも入る。マウス操作中は何もしない
- `PopUpManager.Show` / `ItemPopUpManager.Show`: ロード完了コールバックで `UIFocusManager.AttachSceneScope(...)`。ポップアップがモーダルスコープ（priority 100）になり、B/Esc で OnCancel（閉じるのみの場合は OnConfirm）を呼ぶ。閉じる（アンロード）とスコープが消え、元画面の最後の選択に戻る
- `TitleView.Update`: `Input.anyKeyDown` → `ControllerInput.AnyPressedThisFrame()`（パッドでも開始）

## 5. シーン配線（Tools/Controller/Wire Scene EventSystems で再実行可）
対象: Boot, Title, TomsShop, Village, Preparation, Event, Fight, Result, GameOver の EventSystem。
- `InputSystemUIInputModule.actionsAsset` と各アクション参照を `InputSystem_Actions` の UI マップ（サブアセット参照）に統一（旧: 8 シーンがパッケージの DefaultInputActions、TomsShop はシーン埋め込み参照）
- `ControllerOverlayBootstrap`（overlayPrefab = ControllerOverlay.prefab）を追加
- PopupScene / ItemPopupScene / Setting は EventSystem を持たない（親シーンのものを使う）ので変更なし

## 6. 実装済みの画面
- 店ホーム `TomsShop.prefab`: ルートスコープの first をフェーズで切替（**ルートスコープ dpadOnlyNavigation=ON**: 左スティック・WASD=トム移動のみ、十字・矢印=UI 選択のみ。トムの `PlayerMove` は ignoreArrowKeysAndDpad=ON＝矢印・十字を無視、walkOnlyWhileParentScopeTop=ON＝施設/陳列/ポップアップ等の別スコープが最前面の間は歩かない。別スコープが前面ならスティックでも UI 選択できる）（`TurnPhaseView`: 仕入れ=鍛冶屋 / 陳列=商品陳列 / 営業=戻る。営業開始は first にしない・演出中は自動選択停止）。Cancel=戻る（陳列・営業）。施設から B で戻ると入った施設ボタンへ復帰
  - 近道（ルートスコープが最前面のとき）: **Start＝設定画面**、**キーボード Esc＝「戻る」が無い仕入れ中は設定画面**（`SetCancelFallback`。パッドの B では開かない＝配信画面と同じ規則）、**Y / R＝「次へ」を押す**（押下アニメ・音つき。営業フェーズの「営業開始」は誤爆防止のため近道にしない）。Tab と Space は使わない（Tab は入力機器の判定、Space は Submit と重なる）
  - 流れは「陳列 →『次へ』→ 営業フェーズ →『営業開始』ボタン」。営業開始を押したとき、品出しゼロなら確認「品出しゼロで営業します」（「陳列にもどる」で陳列へ）、配信前日なら一度だけ装備の確認（G-3）を出してから営業ダイジェスト（F-7）へ進む（`TomsShopPresenter.RequestStartSales`）
  - 陳列 `ItemSelectionView`: modal(10), first=先頭行の「並べる」, LB/RB=武器/防具, 行内フォーカスで説明表示（`PadSelectRelay`）。税金(Debt)=modal(20) first=×、破産=modal(30)。朝レポート/レリック3択=コードで modal(20)（レリックは B 無し）
- 鍛冶屋 `BlackSmithView`（コード）: first=一覧先頭行（`ItemShopSlot` に `PointerEventBridge`＝選択で説明・A で銘柄選択）, B=もどる, LB/RB=武器/防具/開発（切替後は新タブの先頭行へフォーカス移動。開発タブは first=タブ自身）, X=行を選択して個数設定（詳細パネルのスライダー）へ移動、個数設定で B=一覧の同じ行へ戻る（一覧上の B は従来どおりもどる）。数量はスライダーで左右。オート購入=modal(10) first=全額・明示ナビ
- マシンショップ/店の改装（コード）: B=閉じる（左上のもどる）, LB/RB=改装／設備のタブ切り替え（`ShopFacilityTabsView`）, first=カタログ先頭 / 閉じる（支払いボタンは first にしない）。ターン終了サマリー: first=次のターンへ。イベント: modal(20) first=はい
- 汎用ポップアップ（`Popup.cs`）: 確認系は「キャンセル」側を初期選択（閉じるのみは閉じる）
- 配信（FightScene）・リザルト（ResultScene）— シーン/プレハブは無変更、すべてコードで実行時に付与
  - 配信設定 `StreamingSettingView`: スコープ（firstSelected = 左一覧の先頭）＋両一覧に `ScrollRectAutoFollow`。`AvailableItemSlot` に `PointerEventBridge`
  - 配信中 `Fight`: `BattlePanelManager` が `BattleGamepadController` を付与（スコープ firstSelected = カードのタブ）。`ItemSlotView` に `PointerEventBridge`。狙う敵は `BattlePadTargetSelector`（敵スプライト上の透明プロキシ＋既存の選択枠, modal priority 40）
  - 補充ポップアップ `RestockQuantityPopup`: modal priority 50, Cancel=やめる, 最初は数量スライダー。スライダーから下で購入ボタン（`ItemDetailPanel.ApplyPadNavigation` で Explicit。Automatic だと下のキャンセルを拾って購入に届かなかった）／配信結果 `BattleResultView`: firstSelected = 確認
  - `BattlePanelManager.ShowPanel` で `NotifyPanelShown`
- 配信のパッド操作
  - 配信設定: 十字で移動、A=左一覧のアイテムを追加／±ボタン、X=右一覧（選択中スロット内）の解除（右クリック代替）
  - 配信中: A=介入ボタン・タブ・スロット（価格グラフ／在庫切れなら補充）、LB/RB=カードをめくる（スワイプ代替）、Start=一時停止して設定画面（加算シーン Setting）を開く。閉じたら再開（開く前から一時停止中なら停止のまま）
  - Y=狙う敵を選ぶ（敵タップ代替）: 十字で敵を切替（選択枠が敵に重なる）→ A 決定 / B やめる。青スパ・赤スパを A で出した直後は敵 2 体以上なら自動で開く
  - X（在庫側）=そのアイテムを売り場の先頭の空き枠へ並べる（D&D で空き枠へ置くのと同じ `OnItemDropped` 経由）。フォーカスは在庫側に残り、元スロットが空になったら最寄りの在庫アイテムへ移る（連打で順に並べられる）。売り場に空きが無ければ持ち上げて売り場の先頭へフォーカス → A で置いた先と入替 / B か X で取消（元の在庫スロットへ戻る）
  - X（売り場側）=従来どおり持ち上げ（半透明＋ゴーストが選択先の上に浮く）→ 置き先で A（D&D と同じ: 売り場内の並べ替え・在庫へ戻す）/ B か X で取消
  - リザルト演出中: A/B でスキップ（`ResultSkipCatcher`）
- コード（View.Awake）で `UIFocusScope.GetOrAdd` する方式（シーン無編集）
  - タイトル: スタート画面=autoSelect OFF（anyKey）/ 開始方法=first「続きから」(無ければニューゲーム)・Cancel=戻る / セーブスロット=first 直前操作スロット(無ければ最初の選べるスロット)・Cancel=戻る・再構築後も同スロットへ。オプション(Setting)は `AttachSceneScope` で B=閉じる
  - 村: ルートスコープ(autoSelect OFF)。Start/Esc/B で HUD ボタン選択モード（first=出店準備、もう一度 B/Start で歩行へ）。投資パネル/収支ポップ=modal・Cancel=閉じる・first=閉じる。UI操作中は PlayerInput を一時停止。施設に入る=キー E/Enter/Space + パッド A（`VillageView.FieldInputAllowed` でUI操作中は無効）
  - 準備: ルート=first「ふつう」・Cancel=戻る（出店は first にしない）。ゲームオーバー: first=リトライ。イベント: eventPanel に付与・first=確認
  - Utage 会話: `GamepadInputStrategy`（A=次へ / B=ウィンドウ・バックログを閉じる / LB=バックログ / RB 押しっぱなし=スキップ）。会話中は `ScenarioPlayer` が BlockScopeInput＋modal スコープ(priority 50)を張り、選択肢表示中だけ先頭の選択肢へフォーカス
- 情報屋/占い師/勇者/新聞/地図/宣伝/ダンジョン支援（各 Screens プレハブの View に `UIFocusScope` priority 1・Cancel=戻る/閉じる。飾りの Button と一覧の Scrollbar は Navigation=None）。行の「選択＝ホバー/行選択」は `ControllerSelectRelay`、作り直す一覧の先頭/同じ行への復帰は `PendingFocus`（`Scripts/Hero/ControllerSelectRelay.cs`）
  - 情報屋: first=地図の先頭行（行フォーカスで詳細表示）・LB/RB=地図/取引所/開発。占い師: first=トレンド先頭行（TrendRow/RankRow に PointerEventBridge＝選択でセリフ）。勇者: first=武器ドロップダウン（選択で説明表示）
  - 新聞: first=一面トップ・LB/RB=面送り・記事ポップアップ=modal(10)/Cancel=閉じる。地図: first=進行中ダンジョン・ダンジョン情報=modal(10)/Cancel=×。宣伝: first=広告先頭。ダンジョン支援: first=先頭行（フォーカスで詳細）・レベルアップ後も同じ行
  - 設定(Setting シーン): `Setting.Awake` でルート Canvas にスコープ（modal, priority 100, Cancel=閉じる, first=BGM スライダー＝左右で音量）・LB/RB=タブ切替

## 7. 注意・既知の点
- `DebugResetManager` は Esc を 2 秒以内に 10 連打するとセーブ削除＋タイトルへ戻る（展示用）。Esc を Cancel に使うので、キーボードで戻るを連打すると誤爆し得る
- Utage 会話中も最前面スコープに Cancel が届く。会話担当は `UIFocusManager.BlockScopeInput()` で止めること
- 村（PlayerInput 有効）ではフォールバック自動選択をしない。UI を出す場合はその UI にスコープを付ける
- UI/Navigate は **Value** 型（PassThrough だとスティックが送り続ける 0 入力で十字キーが打ち消され、十字キーの反応が鈍くなるため変更）
- `dpadOnlyNavigation` はアセット（InputSystem_Actions）の UI/Navigate に実行時のバインドオーバーライドを掛ける（保存されない・終了時にも解除）。パスに "stick" を含むバインドと `<Keyboard>/w,a,s,d` が対象。村のルートスコープは OFF のままなので村の挙動は不変
- 確認ポップアップ（Popup.cs）の確認/キャンセルは左右 Explicit ナビ（Automatic だと背面画面のボタンを拾ってモーダルに戻され、切り替えられない）
- 枠の内側に別ボタンがある部品（セーブスロットの削除・記事のスクラップ・新聞社カードの購読・スクラップカードの確認）は `InnerSelectableNavigation`（枠が選ばれた時に「右=内側ボタン、他=Automatic と同じ」を Explicit で組む）で到達させる

## 8. 検証結果・既知の制限（2026-10-11 仮想パッドで Play 検証）
- OK: タイトル（開始/続きから/スロット/削除確認B/設定 LB・RB・BGM 左右・B）、準備、村（歩行・A で投資・閉じて再オープンなし・Start/B で HUD）、Utage（A 送り・B 隠す→A 復帰・LB ログ→B・RB 長押しスキップ・背面へ Cancel 漏れなし）、情報屋/占い師/勇者/新聞/地図/宣伝/ダンジョン支援、配信設定→配信中（LB/RB・Y 敵選択・X 持ち上げ→A/B・補充ポップ B・Start）→リザルト→店、ゲームオーバー、確認ポップ（キャンセル初期）、マウス復帰で枠消去
- 検証で直した点: Utage の入力戦略が Utage 側（InputSystemStrategy）に上書きされパッドが効かなかった → 会話開始ごとに再登録・基底を InputSystemStrategy に／B で隠したウィンドウを A で戻せなかった／介入カードのボタンが Navigation=None で十字移動できなかった／各施設の立ち絵ボタン（画面より大きい枠）を選ばない／占い師の行・配信設定カードの枠を見た目に合わせた／配信設定の一覧スクロールバーで十字が止まる／所持品ゼロ時の初期フォーカスが「配信開始」だった／タイトルの「戻る」から上が先頭スロットへ飛ぶ
- 店ホームのスティック/十字分離（2026-10-11 仮想パッド）: 店ホームで 十字・矢印=選択移動のみ（トム不動）、スティック・WASD=トム移動のみ（選択不変）。鍛冶屋内ではスティックでも選択移動、B で戻ると再び分離。別スコープ前面中はトム停止
- 未確認: Utage の選択肢（現シナリオに選択肢が無い）、青/赤スパ直後の自動敵選択（敵 2 体以上の場面を作れず）、EventScene（ビルド未登録で到達不可。コードのみ確認）
- 既知: 敵選択枠はスプライトの透明余白込みのためやや上に大きい。配信中の補充ポップアップの上に HP 表示・進行バーが重なる（描画順。パッド対応とは別件）
- 村の吹き出し「[E] しらべる」は `InputPromptText` で、パッド使用中は決定ボタンの表記（Xbox「[A]」/ PlayStation「[×]」/ Nintendo「[B]」）に替わる

## 9. 手触り・音（2026-10 追加）
- ホバー: ColorTint のボタンは `colorMultiplier=1.2` で、ホバー時だけ少し明るい暖色（実効 1.2/1.14/1.02）。通常・押下（約78%）・選択・無効の見え方は従来と同じ（各色を 1.2 で割って保存）。押下で大きさは変えない（PR #77 の方針）
- `-1` 版の素材がある施設ボタン（鍛冶屋・情報屋・マップ・広告・商品陳列・営業開始・メニュー・閉じる・もどる）は `ButtonHoverSprite` でホバー中とパッド選択中だけ `-1` の絵に差し替える（RectTransform は動かさないので選択枠とずれない。押下は ColorTint のまま）
- 飾りに付いていた Button（陳列のアイコン枠・説明欄、鍛冶屋の罫線）は外した
- 操作音: `ButtonSoundHandler` が閉じる/戻る/キャンセル系（名前・ラベル・絵で判定）に「戻る」音、それ以外に決定音を付ける。後から生成される行も拾い、同じボタンへは一度だけ登録（入れ子の Canvas で二重に鳴らない）。ハンドラの無いシーンの Canvas にはロード時に自動で付く（ポップアップ・設定・村・準備・リザルト・配信）。宴のボタン（`UguiButtonSe`）は対象外。B/Esc でポップアップ・設定を閉じたときも戻る音（`AttachSceneScope`）。同じフレームの UI 音は1回（`UISound`）
- 介入ボタンなどのホバー説明（`HoverTooltipTrigger`）はパッド/キーで選択しても出る（要素の右上）
- 開く演出: 確認ポップアップ・税金・陳列・朝レポート・タイトルの画面切替もフェード（モーダルは小さなポップ付き）。全画面の切替は alpha 0.35 から（背景が一瞬抜けるのを抑える）。HUD は新しく出るときだけ本体と同じフェード

## 店ホームのボタンの振り分け（2026-10 レビュー1）
- 仕入れの画面（読み・仕入れ）: 新聞・情報屋・鍛冶屋（左の列）、地図・勇者（右の列）、次へ
- 陳列の画面（店の準備・賭けの仕上げ）: 税金・占い師・商品陳列（左の列）、魔王軍支援・店・広告（右の列）、戻る・次へ
- 営業の画面: 営業開始（大きいボタン）・戻る。営業開始を押すと、品出しゼロなら確認、配信前日なら装備の確認（G-3）、そのあと営業ダイジェスト（F-7）
- ボタンは全部 260×130、列と段のすき間は 10。文字は 36（「魔王軍支援」だけ 33・字間 -4）。魔王軍支援・地図は文字なしの板＋文字にした（ドクロの絵・丸いマップの絵はやめた）
